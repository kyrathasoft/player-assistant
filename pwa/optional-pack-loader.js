'use strict';

// Shared by the page, translator worker, and campaign-search worker.
(() => {
    const CACHE_PREFIX = 'player-assistant-optional-pack-';
    const manifestPromise = new Map();
    const loaded = new Map();

    const sha256 = async (bytes) => {
        if (!globalThis.crypto?.subtle) throw new Error('This browser cannot verify optional packs.');
        const digest = await crypto.subtle.digest('SHA-256', bytes);
        return [...new Uint8Array(digest)].map((value) => value.toString(16).padStart(2, '0')).join('');
    };

    const isRecord = (value) => value !== null && typeof value === 'object' && !Array.isArray(value);
    const expectedMime = (url) => url.endsWith('.json') ? 'application/json' : '';
    const validatePayload = (entry, payload) => {
        if (!isRecord(payload) || payload.schemaVersion !== entry.schemaVersion) return false;
        if (entry.kind === 'translator') {
            return payload.language?.toLocaleLowerCase('en-US') === entry.language
                && payload.entryCount === entry.recordCount
                && payload.maxPhraseWords === entry.validation.maxPhraseWords
                && payload.reverseMaxPhraseWords === entry.validation.reverseMaxPhraseWords
                && isRecord(payload.terms)
                && Object.keys(payload.terms).length === entry.recordCount;
        }
        return payload.pageCount === entry.recordCount
            && Array.isArray(payload.pages)
            && payload.pages.length === entry.recordCount
            && payload.termIndexVersion === entry.validation.termIndexVersion;
    };

    const getManifest = async (manifestUrl = 'optional-packs.json') => {
        if (!manifestPromise.has(manifestUrl)) {
            manifestPromise.set(manifestUrl, fetch(manifestUrl, { cache: 'no-store' })
                .then(async (response) => {
                    if (!response.ok || !(response.headers.get('Content-Type') || '').includes('json')) {
                        throw new Error(`Optional-pack manifest returned ${response.status}.`);
                    }
                    const manifest = await response.json();
                    if (manifest.schemaVersion !== 1 || !Array.isArray(manifest.packs)) throw new Error('Optional-pack manifest is invalid.');
                    const ids = new Set(manifest.packs.map((pack) => pack.id));
                    if (ids.size !== manifest.packs.length) throw new Error('Optional-pack manifest contains duplicate IDs.');
                    return manifest;
                }));
        }
        return manifestPromise.get(manifestUrl);
    };

    const fetchPack = async (requestUrl, attempts = 3) => {
        let lastError;
        for (let attempt = 0; attempt < attempts; attempt += 1) {
            try {
                const response = await fetch(requestUrl, { cache: 'no-store' });
                if (response.ok || ![408, 429, 500, 502, 503, 504].includes(response.status)) return response;
                lastError = new Error(`Optional pack returned ${response.status}.`);
            } catch (error) {
                lastError = error;
            }
            if (attempt + 1 < attempts) await new Promise((resolve) => setTimeout(resolve, 250 * (2 ** attempt)));
        }
        throw lastError || new Error('Optional pack request failed.');
    };

    const loadPack = async (id, { manifestUrl = 'optional-packs.json', force = false } = {}) => {
        const cacheName = `${CACHE_PREFIX}${id}`;
        const manifest = await getManifest(manifestUrl);
        const entry = manifest.packs.find((pack) => pack.id === id);
        if (!entry) throw new Error(`Optional pack '${id}' is not declared.`);
        if (!force && loaded.has(id)) return loaded.get(id);
        const requestUrl = new URL(entry.url, new URL(manifestUrl, globalThis.location?.href || entry.url));
        const cacheKey = `${requestUrl.href}?pack-hash=${entry.contentHash}`;
        const cache = globalThis.caches ? await caches.open(cacheName) : null;
        const read = async (response) => {
            if (!response?.ok) throw new Error(`Optional pack '${id}' returned ${response?.status || 0}.`);
            const contentType = response.headers.get('Content-Type') || '';
            if (expectedMime(entry.url) && !contentType.toLowerCase().includes(expectedMime(entry.url))) throw new Error(`Optional pack '${id}' has the wrong MIME type.`);
            const bytes = await response.clone().arrayBuffer();
            if (bytes.byteLength !== entry.byteSize || await sha256(bytes) !== entry.contentHash) throw new Error(`Optional pack '${id}' failed content verification.`);
            const payload = JSON.parse(new TextDecoder().decode(bytes));
            if (!validatePayload(entry, payload)) throw new Error(`Optional pack '${id}' failed schema validation.`);
            return { payload, response };
        };
        let validated = null;
        if (cache && !force) {
            const cached = await cache.match(cacheKey);
            try { validated = cached ? await read(cached) : null; } catch { await cache.delete(cacheKey); }
        }
        if (!validated) {
            validated = await read(await fetchPack(requestUrl));
            if (cache) {
                try {
                    await cache.put(cacheKey, validated.response.clone());
                    const keys = await cache.keys();
                    await Promise.all(keys
                        .filter((key) => key.url.startsWith(requestUrl.origin + requestUrl.pathname)
                            && key.url !== cacheKey)
                        .map((key) => cache.delete(key)));
                } catch (error) { if (error?.name !== 'QuotaExceededError') throw error; }
            }
        }
        const payload = validated.payload;
        loaded.set(id, payload);
        return payload;
    };

    const removePack = async (id) => {
        loaded.delete(id);
        if (globalThis.caches) await caches.delete(`${CACHE_PREFIX}${id}`);
    };

    globalThis.PlayerAssistantPackLoader = Object.freeze({ getManifest, loadPack, removePack, validatePayload });
})();
