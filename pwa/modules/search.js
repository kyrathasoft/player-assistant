'use strict';

const MAX_SEARCH_RESULTS = 40;
const SEARCH_EXPRESSION_CACHE_LIMIT = 128;
const searchWordCharacters = "\\p{L}\\p{N}'’-";

export const createSearchExpression = (term) => {
    const leadingWildcard = term.startsWith('*');
    const trailingWildcard = term.endsWith('*');
    const core = term
        .split('*')
        .map((value) => value.replace(/[.*+?^${}()|[\]\\]/gu, '\\$&'))
        .join(`[${searchWordCharacters}]*`);
    if (!core) return null;
    const prefix = leadingWildcard ? '' : `(^|[^${searchWordCharacters}])`;
    const suffix = trailingWildcard ? '' : `(?=$|[^${searchWordCharacters}])`;
    return new RegExp(`${prefix}${core}${suffix}`, 'iu');
};

export const createSearchExpressionCache = (createExpression = createSearchExpression) => {
    const expressions = new Map();
    return (term) => {
        if (expressions.has(term)) return expressions.get(term);
        const expression = createExpression(term);
        if (expressions.size >= SEARCH_EXPRESSION_CACHE_LIMIT) {
            expressions.delete(expressions.keys().next().value);
        }
        expressions.set(term, expression);
        return expression;
    };
};

const normalizeSearchQuery = (value) => String(value || '')
    .normalize('NFKC')
    .replaceAll('’', "'")
    .toLocaleLowerCase('en-US')
    .replace(/[^\p{L}\p{N}'*-]+/gu, ' ')
    .replace(/\s+/gu, ' ')
    .trim();

const buildSearchSnippet = (entry, queryTerms) => {
    if (!entry.content) return 'Title match';
    const lowerContent = entry.content.toLocaleLowerCase('en-US');
    const matchIndex = queryTerms
        .map((term) => lowerContent.indexOf(term.replaceAll('*', '')))
        .filter((index) => index >= 0)
        .sort((left, right) => left - right)[0] ?? 0;
    const visibleQueryLength = Math.max(1, ...queryTerms.map((term) => term.replaceAll('*', '').length));
    const start = Math.max(0, matchIndex - 70);
    const end = Math.min(entry.content.length, matchIndex + visibleQueryLength + 110);
    const prefix = start > 0 ? '…' : '';
    const suffix = end < entry.content.length ? '…' : '';
    return `${prefix}${entry.content.slice(start, end).trim()}${suffix}`;
};

export const initializeCampaignSearch = ({ byId }) => {
    const searchInput = byId('campaign-search');
    const results = byId('search-results');
    const guidance = byId('search-guidance');
    const removePackButton = byId('campaign-search-remove-pack');
    const retryPackButton = byId('campaign-search-retry-pack');
    const worker = typeof Worker !== 'undefined'
        ? new Worker('campaign-search-worker.js?v=80')
        : null;
    let searchRequestId = 0;
    let searchDebounce = 0;

    const renderMatches = (entries, queryTerms, originalQuery) => {
        if (entries.length === 0) {
            const empty = document.createElement('p');
            empty.className = 'empty-state';
            empty.textContent = `No public campaign pages matched “${originalQuery}”.`;
            results.append(empty);
            return;
        }

        const fragment = document.createDocumentFragment();
        entries.slice(0, MAX_SEARCH_RESULTS).forEach((entry) => {
            const link = document.createElement('a');
            link.className = 'search-result';
            link.href = entry.url;
            link.target = '_blank';
            link.rel = 'noopener noreferrer';
            const title = document.createElement('strong');
            title.textContent = entry.title;
            const snippet = document.createElement('span');
            snippet.className = 'search-result-snippet';
            snippet.textContent = buildSearchSnippet(entry, queryTerms);
            const hint = document.createElement('small');
            hint.textContent = 'Open ↗';
            link.append(title, snippet, hint);
            fragment.append(link);
        });
        results.append(fragment);
    };

    worker?.addEventListener('message', (event) => {
        const message = event.data || {};
        if (message.type === 'pack-status') {
            if (guidance) guidance.textContent = message.message;
            if (retryPackButton) {
                retryPackButton.hidden = !['unavailable', 'retrying', 'stale', 'removed'].includes(message.state);
                retryPackButton.disabled = message.state === 'retrying';
            }
            return;
        }
        if (message.type !== 'search-results' || message.id !== searchRequestId) return;
        if (message.error) {
            if (guidance) guidance.textContent = `Campaign search is unavailable: ${message.error}`;
            return;
        }
        if (!(searchInput instanceof HTMLInputElement) || results === null) return;
        const queryTerms = [...new Set(normalizeSearchQuery(searchInput.value).split(' ').filter(Boolean))];
        renderMatches(message.results || [], queryTerms, searchInput.value.trim());
    });

    const renderSearchResults = () => {
        if (!(searchInput instanceof HTMLInputElement) || results === null) return;
        const query = searchInput.value.trim();
        const normalizedQuery = normalizeSearchQuery(query);
        const requestId = ++searchRequestId;
        results.replaceChildren();
        if (normalizedQuery.length < 2) return;
        if (!worker) {
            if (guidance) guidance.textContent = 'Campaign search is unavailable: background workers are unsupported.';
            return;
        }
        worker.postMessage({ type: 'search', id: requestId, query });
    };

    searchInput?.addEventListener('input', () => {
        window.clearTimeout(searchDebounce);
        searchDebounce = window.setTimeout(renderSearchResults, 100);
    });

    retryPackButton?.addEventListener('click', () => {
        worker?.postMessage({ type: 'retry-pack' });
    });
    removePackButton?.addEventListener('click', () => {
        worker?.postMessage({ type: 'clear-pack' });
        if (results) results.replaceChildren();
        if (guidance) guidance.textContent = 'Campaign search pack removed. It will download again when needed.';
    });

    return Object.freeze({ load: () => Promise.resolve([]) });
};
