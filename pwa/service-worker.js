'use strict';

const CACHE_VERSION = 'player-assistant-pwa-0.9.5-v28';
const SHELL_CACHE = `${CACHE_VERSION}-shell`;
const DATA_CACHE = `${CACHE_VERSION}-data`;
const SHELL_ASSETS = [
    './',
    './index.html',
    './styles.css?v=28',
    './app.js?v=28',
    './translator-worker.js',
    './offline.html',
    './manifest.webmanifest',
    './icons/icon-192.png',
    './icons/icon-512.png',
    './icons/dragon-mark.png'
];

self.addEventListener('install', (event) => {
    event.waitUntil(
        caches.open(SHELL_CACHE)
            .then((cache) => cache.addAll(
                SHELL_ASSETS.map((asset) => new Request(asset, { cache: 'reload' }))))
            .then(() => self.skipWaiting())
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
    const cache = await caches.open(cacheName);
    const cached = await cache.match(request);
    if (cached) return cached;
    const response = await fetch(request);
    if (response.ok) await cache.put(request, response.clone());
    return response;
};

const networkFirstData = async (request) => {
    const cache = await caches.open(DATA_CACHE);
    try {
        const response = await fetch(new Request(request, { cache: 'reload' }));
        if (response.ok) await cache.put(request, response.clone());
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
        if (response.ok) await cache.put('./index.html', response.clone());
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

    if (url.pathname.includes('/data/') || url.pathname.endsWith('/campaign-search.json')) {
        event.respondWith(cacheFirst(request, DATA_CACHE));
        return;
    }

    event.respondWith(cacheFirst(request, SHELL_CACHE));
});
