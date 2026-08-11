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

export const initializeCampaignSearch = ({ byId }) => {
    let campaignSearchIndex = null;
    let campaignSearchLoading = null;
    const getCachedSearchExpression = createSearchExpressionCache();

    async function loadCampaignSearch() {
        if (campaignSearchIndex) {
            return campaignSearchIndex;
        }
        if (campaignSearchLoading) {
            return campaignSearchLoading;
        }

        campaignSearchLoading = fetch('campaign-search.json')
            .then((response) => {
                if (!response.ok) throw new Error(`Search data returned ${response.status}.`);
                return response.json();
            })
            .then((data) => {
                const sourceEntries = Array.isArray(data.pages)
                    ? data.pages
                    : Object.entries(data).map(([title, url]) => ({ title, url, content: '' }));
                campaignSearchIndex = sourceEntries
                    .map((entry) => ({
                        title: String(entry.title || ''),
                        url: String(entry.url || ''),
                        content: String(entry.content || '')
                    }))
                    .filter((entry) => entry.title.length > 0 && /^https:\/\//i.test(entry.url))
                    .map((entry) => ({
                        ...entry,
                        normalizedTitle: normalizeSearchText(entry.title),
                        normalizedContent: normalizeSearchText(entry.content)
                    }))
                    .map((entry) => ({
                        ...entry,
                        normalizedCombined: `${entry.normalizedTitle} ${entry.normalizedContent}`.trim()
                    }));
                return campaignSearchIndex;
            })
            .catch((error) => {
                const guidance = byId('search-guidance');
                if (guidance) guidance.textContent = `Campaign search is unavailable: ${error.message}`;
                return [];
            });
        return campaignSearchLoading;
    }

    const normalizeSearchText = (value) => String(value || '')
        .normalize('NFKC')
        .replaceAll('’', "'")
        .toLocaleLowerCase('en-US')
        .replace(/[^\p{L}\p{N}'-]+/gu, ' ')
        .replace(/\s+/gu, ' ')
        .trim();

    const normalizeSearchQuery = (value) => String(value || '')
        .normalize('NFKC')
        .replaceAll('’', "'")
        .toLocaleLowerCase('en-US')
        .replace(/[^\p{L}\p{N}'*-]+/gu, ' ')
        .replace(/\s+/gu, ' ')
        .trim();

    const matchesSearchTerm = (text, term) => getCachedSearchExpression(term)?.test(text) === true;

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

    const renderSearchResults = async () => {
        const searchInput = byId('campaign-search');
        const results = byId('search-results');
        if (!(searchInput instanceof HTMLInputElement) || results === null) {
            return;
        }

        const query = searchInput.value.trim();
        const normalizedQuery = normalizeSearchQuery(query);
        results.replaceChildren();
        if (normalizedQuery.length < 2) {
            return;
        }

        const queryTerms = [...new Set(normalizedQuery.split(' ').filter(Boolean))];
        const literalQuery = normalizedQuery.replaceAll('*', '').replace(/\s+/gu, ' ').trim();
        const hasWildcards = normalizedQuery.includes('*');
        const entries = await loadCampaignSearch();
        const matches = entries
            .map((entry) => {
                const title = entry.normalizedTitle;
                const content = entry.normalizedContent;
                const combined = entry.normalizedCombined;
                const titleMatchesAll = queryTerms.every((term) => matchesSearchTerm(title, term));
                const allTermsMatch = queryTerms.every((term) => matchesSearchTerm(combined, term));
                const score = !hasWildcards && title === literalQuery ? 0
                    : matchesSearchTerm(title, normalizedQuery) ? 10
                    : titleMatchesAll ? 20
                    : matchesSearchTerm(content, normalizedQuery) ? 30
                    : allTermsMatch ? 40
                    : 99;
                return { ...entry, score };
            })
            .filter((entry) => entry.score < 99)
            .sort((left, right) => left.score - right.score || left.title.localeCompare(right.title))
            .slice(0, MAX_SEARCH_RESULTS);

        if (matches.length === 0) {
            const empty = document.createElement('p');
            empty.className = 'empty-state';
            empty.textContent = `No public campaign pages matched “${searchInput.value.trim()}”.`;
            results.append(empty);
            return;
        }

        const fragment = document.createDocumentFragment();
        matches.forEach((entry) => {
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

    let searchDebounce = 0;
    byId('campaign-search')?.addEventListener('input', () => {
        window.clearTimeout(searchDebounce);
        searchDebounce = window.setTimeout(renderSearchResults, 100);
    });

    return Object.freeze({ load: loadCampaignSearch });
};
