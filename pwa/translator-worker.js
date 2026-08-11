'use strict';

const lexicons = new Map();
const pendingLexicons = new Map();
const LEXICON_CACHE_DB_NAME = 'player-assistant-lexicons';
const LEXICON_CACHE_DB_VERSION = 1;
const LEXICON_CACHE_STORE = 'compiled';
const wordPattern = /[\p{L}\p{N}&]+(?:['’\-][\p{L}\p{N}]+)*/gu;

const normalize = (value) => value
    .normalize('NFKC')
    .trim()
    .replace(/\s+/gu, ' ')
    .toLocaleLowerCase('en-US');

const applySourceCapitalization = (source, translation) => {
    if (!translation || !source) return translation;
    if (source === source.toLocaleUpperCase() && /\p{L}/u.test(source)) {
        return translation.toLocaleUpperCase();
    }
    const firstLetter = source.match(/\p{L}/u)?.[0];
    if (firstLetter && firstLetter === firstLetter.toLocaleUpperCase()) {
        return translation.replace(/\p{L}/u, (letter) => letter.toLocaleUpperCase());
    }
    return translation;
};

const languageName = (language) => language === 'elvish'
    ? 'Elvish'
    : language === 'ghukliak' ? 'Goblin (Ghukliak)' : 'Orcish';

const openLexiconCache = () => {
    if (typeof indexedDB === 'undefined') return Promise.resolve(null);
    return new Promise((resolve, reject) => {
        const request = indexedDB.open(LEXICON_CACHE_DB_NAME, LEXICON_CACHE_DB_VERSION);
        request.onupgradeneeded = () => {
            if (!request.result.objectStoreNames.contains(LEXICON_CACHE_STORE)) {
                request.result.createObjectStore(LEXICON_CACHE_STORE);
            }
        };
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => reject(request.error);
    });
};

const readCompiledLexicon = async (cacheKey, payload) => {
    try {
        const database = await openLexiconCache();
        if (!database) return null;
        const record = await new Promise((resolve, reject) => {
            const transaction = database.transaction(LEXICON_CACHE_STORE, 'readonly');
            const request = transaction.objectStore(LEXICON_CACHE_STORE).get(cacheKey);
            request.onsuccess = () => resolve(request.result);
            request.onerror = () => reject(request.error);
        });
        if (!record
            || record.cacheKey !== cacheKey
            || record.schemaVersion !== payload.schemaVersion
            || record.contentHash !== payload.contentHash
            || !Array.isArray(record.forward)
            || !Array.isArray(record.reverse)) {
            return null;
        }
        return {
            forward: new Map(record.forward),
            reverse: new Map(record.reverse)
        };
    } catch {
        return null;
    }
};

const writeCompiledLexicon = async (cacheKey, language, payload, forward, reverse) => {
    try {
        const database = await openLexiconCache();
        if (!database) return;
        await new Promise((resolve, reject) => {
            const transaction = database.transaction(LEXICON_CACHE_STORE, 'readwrite');
            const store = transaction.objectStore(LEXICON_CACHE_STORE);
            const keysRequest = store.getAllKeys();
            keysRequest.onsuccess = () => {
                keysRequest.result
                    .filter((key) => typeof key === 'string' && key.startsWith(`${language}:`) && key !== cacheKey)
                    .forEach((key) => store.delete(key));
                store.put({
                    cacheKey,
                    schemaVersion: payload.schemaVersion,
                    contentHash: payload.contentHash,
                    forward: [...forward],
                    reverse: [...reverse]
                }, cacheKey);
            };
            keysRequest.onerror = () => reject(keysRequest.error);
            transaction.oncomplete = resolve;
            transaction.onerror = () => reject(transaction.error);
        });
    } catch {
        // IndexedDB is an optional optimization; translation remains network/HTTP-cache backed.
    }
};

const createLexicon = (payload, forward, reverse) => ({
    forward,
    reverse,
    forwardMaxPhraseWords: Math.max(1, Number(payload.maxPhraseWords) || 1),
    reverseMaxPhraseWords: Number.isInteger(Number(payload.reverseMaxPhraseWords))
        && Number(payload.reverseMaxPhraseWords) >= 1
        ? Number(payload.reverseMaxPhraseWords)
        : getMaxPhraseWords(reverse),
    entryCount: Number(payload.entryCount) || forward.size
});

const loadLexicon = async (language) => {
    if (lexicons.has(language)) return lexicons.get(language);
    if (pendingLexicons.has(language)) return pendingLexicons.get(language);

    const request = (async () => {
        self.postMessage({ type: 'status', loading: true, message: `Loading ${languageName(language)} lexicon…` });
        const response = await fetch(`data/${language}.json`);
        if (!response.ok) throw new Error(`${language} lexicon returned ${response.status}`);
        const payload = await response.json();
        const contentHash = typeof payload.contentHash === 'string'
            && /^[a-f0-9]{64}$/u.test(payload.contentHash)
            ? payload.contentHash
            : null;
        const cacheKey = contentHash ? `${language}:${payload.schemaVersion}:${contentHash}` : null;
        const cached = cacheKey ? await readCompiledLexicon(cacheKey, payload) : null;
        let lexicon;
        if (cached) {
            lexicon = createLexicon(payload, cached.forward, cached.reverse);
        } else {
            const forward = new Map();
            const reverse = new Map();
            Object.entries(payload.terms || {}).forEach(([english, translated]) => {
                const englishKey = normalize(english);
                const translatedValue = String(translated);
                forward.set(englishKey, translatedValue);
                const reverseKey = normalize(translatedValue);
                if (reverseKey && !reverse.has(reverseKey)) reverse.set(reverseKey, english);
            });
            lexicon = createLexicon(payload, forward, reverse);
            if (cacheKey) void writeCompiledLexicon(cacheKey, language, payload, forward, reverse);
        }
        lexicons.set(language, lexicon);
        self.postMessage({
            type: 'status',
            loading: false,
            message: `${languageName(language)} lexicon ready · ${lexicon.entryCount.toLocaleString()} terms`
        });
        return lexicon;
    })().finally(() => pendingLexicons.delete(language));

    pendingLexicons.set(language, request);
    return request;
};

const tokenize = (text) => {
    wordPattern.lastIndex = 0;
    const tokens = [];
    let match;
    while ((match = wordPattern.exec(text)) !== null) {
        tokens.push({ value: match[0], start: match.index, end: match.index + match[0].length });
    }
    return tokens;
};

const translateText = (text, dictionary, maxPhraseWords) => {
    const tokens = tokenize(text);
    if (tokens.length === 0) return text;

    let result = '';
    let cursor = 0;
    let tokenIndex = 0;
    while (tokenIndex < tokens.length) {
        const first = tokens[tokenIndex];
        result += text.slice(cursor, first.start);
        let selected = null;
        const remaining = tokens.length - tokenIndex;
        const maximum = Math.min(maxPhraseWords, remaining);

        for (let length = maximum; length >= 1; length--) {
            const last = tokens[tokenIndex + length - 1];
            let contiguous = true;
            for (let offset = 0; offset < length - 1; offset++) {
                const left = tokens[tokenIndex + offset];
                const right = tokens[tokenIndex + offset + 1];
                if (!/^\s+$/u.test(text.slice(left.end, right.start))) {
                    contiguous = false;
                    break;
                }
            }
            if (!contiguous) continue;

            const sourcePhrase = text.slice(first.start, last.end);
            const normalizedPhrase = normalize(sourcePhrase);
            const translated = dictionary.get(normalizedPhrase);
            if (translated !== undefined) {
                selected = { length, last, sourcePhrase, translated };
                break;
            }
        }

        if (selected) {
            result += applySourceCapitalization(selected.sourcePhrase, selected.translated);
            cursor = selected.last.end;
            tokenIndex += selected.length;
        } else {
            result += first.value;
            cursor = first.end;
            tokenIndex++;
        }
    }

    return result + text.slice(cursor);
};

const getMaxPhraseWords = (dictionary) => {
    let maximum = 1;
    dictionary.forEach((_translation, source) => {
        maximum = Math.max(maximum, source.split(/\s+/u).length);
    });
    return maximum;
};

self.addEventListener('message', async (event) => {
    const message = event.data || {};
    const language = message.language === 'elvish'
        ? 'elvish'
        : message.language === 'ghukliak' ? 'ghukliak' : 'orcish';
    if (message.type === 'preload') {
        try {
            await loadLexicon(language);
        } catch (error) {
            self.postMessage({ type: 'status', loading: false, message: `${languageName(language)} lexicon unavailable: ${error.message}` });
        }
        return;
    }

    if (message.type !== 'translate') return;
    try {
        const lexicon = await loadLexicon(language);
        const dictionary = message.reverse ? lexicon.reverse : lexicon.forward;
        const maxPhraseWords = message.reverse
            ? lexicon.reverseMaxPhraseWords
            : lexicon.forwardMaxPhraseWords;
        const translation = translateText(String(message.text || ''), dictionary, maxPhraseWords);
        self.postMessage({ type: 'translation', id: message.id, translation });
    } catch (error) {
        self.postMessage({ type: 'translation', id: message.id, error: error.message || String(error) });
    }
});
