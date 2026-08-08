'use strict';

importScripts('./version.js?v=1');

const VERSION_METADATA = globalThis.PLAYER_ASSISTANT_VERSION_METADATA;
if (!VERSION_METADATA) {
    throw new Error('Player Assistant version metadata is unavailable.');
}
const CACHE_VERSION = `player-assistant-pwa-${VERSION_METADATA.pwaVersion}-v${VERSION_METADATA.cacheRevision}`;
const SHELL_CACHE = `${CACHE_VERSION}-shell`;
const DATA_CACHE = `${CACHE_VERSION}-data`;
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
        if (cached?.ok) return cached;
        if (cached) await cache.delete(request);
    } catch {
        cache = null;
    }
    const response = await fetch(request);
    if (response.ok && cache) await safeCachePut(cache, request, response);
    return response;
};

const networkFirstData = async (request) => {
    const cache = await caches.open(DATA_CACHE);
    try {
        const response = await fetch(new Request(request, { cache: 'reload' }));
        if (response.ok) await safeCachePut(cache, request, response);
        return response;
    } catch {
        const cached = await cache.match(request);
        if (cached) return cached;
        throw new Error('Network and cached PWA data are unavailable.');
    }
};

const networkFirstNavigation = async (request) => {
    const cache = await caches.open(SHELL_CACHE);
    try {
        const response = await fetch(request);
        if (response.ok) await safeCachePut(cache, './index.html', response);
        return response;
    } catch {
        return (await cache.match('./index.html')) || (await cache.match('./offline.html'));
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
