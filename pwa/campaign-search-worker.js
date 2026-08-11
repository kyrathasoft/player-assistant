'use strict';

const MAX_SEARCH_RESULTS = 40;
const SEARCH_EXPRESSION_CACHE_LIMIT = 128;
const searchWordCharacters = "\\p{L}\\p{N}'’-";
let campaignSearchIndex = null;
let campaignSearchLoading = null;
const expressions = new Map();

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

const createSearchExpression = (term) => {
    if (expressions.has(term)) return expressions.get(term);
    const leadingWildcard = term.startsWith('*');
    const trailingWildcard = term.endsWith('*');
    const core = term
        .split('*')
        .map((value) => value.replace(/[.*+?^${}()|[\]\\]/gu, '\\$&'))
        .join(`[${searchWordCharacters}]*`);
    const expression = core
        ? new RegExp(
            `${leadingWildcard ? '' : `(^|[^${searchWordCharacters}])`}${core}${trailingWildcard ? '' : `(?=$|[^${searchWordCharacters}])`}`,
            'iu'
        )
        : null;
    if (expressions.size >= SEARCH_EXPRESSION_CACHE_LIMIT) {
        expressions.delete(expressions.keys().next().value);
    }
    expressions.set(term, expression);
    return expression;
};

const matchesSearchTerm = (text, term) => createSearchExpression(term)?.test(text) === true;

const isValidTermIndex = (termIndex, pageCount) => {
    if (!termIndex || typeof termIndex !== 'object' || Array.isArray(termIndex)) return false;
    return Object.values(termIndex).every((pageIds) => Array.isArray(pageIds)
        && pageIds.every((pageId) => Number.isInteger(pageId) && pageId >= 0 && pageId < pageCount));
};

const getCandidateEntries = (entries, queryTerms, hasWildcards, termIndex) => {
    if (hasWildcards || !isValidTermIndex(termIndex, entries.length)) return entries;
    let candidateIds = null;
    for (const term of queryTerms) {
        const postings = termIndex[term];
        if (!Array.isArray(postings)) return [];
        candidateIds = candidateIds === null
            ? [...postings]
            : candidateIds.filter((pageId) => postings.includes(pageId));
        if (candidateIds.length === 0) return [];
    }
    return candidateIds.map((pageId) => entries[pageId]).filter(Boolean);
};

const loadCampaignSearch = async () => {
    if (campaignSearchIndex) return campaignSearchIndex;
    if (campaignSearchLoading) return campaignSearchLoading;
    campaignSearchLoading = fetch('campaign-search.json')
        .then((response) => {
            if (!response.ok) throw new Error(`Search data returned ${response.status}.`);
            return response.json();
        })
        .then((data) => {
            const sourceEntries = Array.isArray(data.pages)
                ? data.pages
                : Object.entries(data).map(([title, url]) => ({ title, url, content: '' }));
            const entries = sourceEntries
                .map((entry) => ({
                    title: String(entry.title || ''),
                    url: String(entry.url || ''),
                    content: String(entry.content || '')
                }))
                .filter((entry) => entry.title.length > 0 && /^https:\/\//i.test(entry.url))
                .map((entry) => {
                    const normalizedTitle = normalizeSearchText(entry.title);
                    const normalizedContent = normalizeSearchText(entry.content);
                    return {
                        ...entry,
                        normalizedTitle,
                        normalizedContent,
                        normalizedCombined: `${normalizedTitle} ${normalizedContent}`.trim()
                    };
                });
            const termIndex = data.termIndexVersion === 1 && isValidTermIndex(data.termIndex, entries.length)
                ? data.termIndex
                : null;
            campaignSearchIndex = { entries, termIndex };
            return campaignSearchIndex;
        })
        .finally(() => {
            campaignSearchLoading = null;
        });
    return campaignSearchLoading;
};

const searchCampaign = async (query) => {
    const normalizedQuery = normalizeSearchQuery(query);
    if (normalizedQuery.length < 2) return [];
    const queryTerms = [...new Set(normalizedQuery.split(' ').filter(Boolean))];
    const literalQuery = normalizedQuery.replaceAll('*', '').replace(/\s+/gu, ' ').trim();
    const hasWildcards = normalizedQuery.includes('*');
    const searchData = await loadCampaignSearch();
    const entries = getCandidateEntries(
        searchData.entries,
        queryTerms,
        hasWildcards,
        searchData.termIndex);
    return entries
        .map((entry) => {
            const titleMatchesAll = queryTerms.every((term) => matchesSearchTerm(entry.normalizedTitle, term));
            const allTermsMatch = queryTerms.every((term) => matchesSearchTerm(entry.normalizedCombined, term));
            const score = !hasWildcards && entry.normalizedTitle === literalQuery ? 0
                : matchesSearchTerm(entry.normalizedTitle, normalizedQuery) ? 10
                : titleMatchesAll ? 20
                : matchesSearchTerm(entry.normalizedContent, normalizedQuery) ? 30
                : allTermsMatch ? 40
                : 99;
            return { title: entry.title, url: entry.url, content: entry.content, score };
        })
        .filter((entry) => entry.score < 99)
        .sort((left, right) => left.score - right.score || left.title.localeCompare(right.title))
        .slice(0, MAX_SEARCH_RESULTS);
};

self.addEventListener('message', async (event) => {
    const message = event.data || {};
    if (message.type !== 'search') return;
    try {
        const results = await searchCampaign(message.query);
        self.postMessage({ type: 'search-results', id: message.id, results });
    } catch (error) {
        self.postMessage({
            type: 'search-results',
            id: message.id,
            error: error.message || String(error),
            results: []
        });
    }
});
