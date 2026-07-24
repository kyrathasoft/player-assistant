(() => {
    'use strict';

    const APP_NAME = 'Player Assistant';
    const APP_VERSION = '0.9.5';
    const AUTH_API_ROOT = '/scarlethorizons/api/v1';
    const MAX_SEARCH_RESULTS = 40;
    const MAX_TRANSLATOR_WORDS = 5000;
    const textEncoder = new TextEncoder();

    const byId = (id) => document.getElementById(id);
    const views = new Map(
        [...document.querySelectorAll('[data-view-panel]')]
            .map((element) => [element.dataset.viewPanel, element]));
    const navButtons = [...document.querySelectorAll('[data-view]')];

    let deferredInstallPrompt = null;
    let translatorRequestId = 0;
    let translatorDebounce = 0;
    let campaignSearchIndex = null;
    let campaignSearchLoading = null;
    let authenticatedAccount = null;
    let authenticationCsrfToken = '';

    const worker = typeof Worker !== 'undefined'
        ? new Worker('translator-worker.js')
        : null;

    const setView = (viewName, updateHistory = true) => {
        const resolvedView = views.has(viewName) ? viewName : 'dashboard';
        views.forEach((panel, name) => {
            const active = name === resolvedView;
            panel.hidden = !active;
            panel.classList.toggle('is-active', active);
        });

        navButtons.forEach((button) => {
            const active = button.dataset.view === resolvedView;
            button.classList.toggle('is-active', active);
            if (active) {
                button.setAttribute('aria-current', 'page');
            } else {
                button.removeAttribute('aria-current');
            }
        });

        if (updateHistory) {
            history.pushState({ view: resolvedView }, '', `#${resolvedView}`);
        }

        if (resolvedView === 'search') {
            void loadCampaignSearch();
        }

        byId('main-content')?.focus({ preventScroll: true });
        window.scrollTo({ top: 0, behavior: 'smooth' });
        document.title = resolvedView === 'dashboard'
            ? APP_NAME
            : `${resolvedView[0].toUpperCase()}${resolvedView.slice(1)} · ${APP_NAME}`;
    };

    navButtons.forEach((button) => {
        button.addEventListener('click', () => setView(button.dataset.view || 'dashboard'));
    });

    document.querySelectorAll('[data-open-view]').forEach((button) => {
        button.addEventListener('click', () => setView(button.dataset.openView || 'dashboard'));
    });

    window.addEventListener('popstate', () => setView(location.hash.slice(1), false));

    const updateConnectionStatus = () => {
        const online = navigator.onLine;
        byId('connection-status')?.classList.toggle('is-offline', !online);
        const label = byId('connection-label');
        if (label) {
            label.textContent = online ? 'Online' : 'Offline';
        }
    };

    window.addEventListener('online', updateConnectionStatus);
    window.addEventListener('offline', updateConnectionStatus);
    updateConnectionStatus();

    const authButton = byId('auth-button');
    const authDialog = byId('auth-dialog');
    const authLoginForm = byId('auth-login-form');
    const authAccountPanel = byId('auth-account-panel');

    const setAuthenticationMessage = (message, isError = false, accountPanel = false) => {
        const element = byId(accountPanel ? 'auth-account-message' : 'auth-message');
        if (element) {
            element.textContent = message;
            element.classList.toggle('is-error', isError);
        }
    };

    const updateAuthenticationUi = () => {
        const authenticated = authenticatedAccount !== null;
        const buttonLabel = byId('auth-button-label');
        if (buttonLabel) {
            buttonLabel.textContent = authenticated
                ? authenticatedAccount.character_name
                : 'Log in';
        }
        authButton?.classList.toggle('is-authenticated', authenticated);
        if (authLoginForm instanceof HTMLFormElement) authLoginForm.hidden = authenticated;
        if (authAccountPanel) authAccountPanel.hidden = !authenticated;
        const accountName = byId('auth-account-name');
        const accountRole = byId('auth-account-role');
        if (accountName) accountName.textContent = authenticatedAccount?.character_name || '';
        if (accountRole) {
            accountRole.textContent = authenticatedAccount?.role === 'dm'
                ? 'Dungeon Master'
                : 'Player';
        }
        const protectedStatus = byId('protected-player-status');
        if (protectedStatus) {
            protectedStatus.textContent = authenticated
                ? `Signed in as ${authenticatedAccount.character_name}. Protected requests are authorized from this server session.`
                : 'Log in with your character name and password. The server determines which character record the session may access; passwords and private records are never embedded in this browser application.';
        }
    };

    const requestAuthenticationApi = async (path, options = {}) => {
        const method = options.method || 'GET';
        const headers = new Headers({ Accept: 'application/json' });
        if (options.body !== undefined) headers.set('Content-Type', 'application/json');
        if (options.csrf === true && authenticationCsrfToken) {
            headers.set('X-CSRF-Token', authenticationCsrfToken);
        }
        let response;
        try {
            response = await fetch(`${AUTH_API_ROOT}${path}`, {
                method,
                headers,
                body: options.body === undefined ? undefined : JSON.stringify(options.body),
                credentials: 'same-origin',
                cache: 'no-store',
                redirect: 'error'
            });
        } catch {
            throw new Error('The character login service is unavailable.');
        }
        let payload = {};
        try {
            payload = await response.json();
        } catch {
            throw new Error('The character login service returned an invalid response.');
        }
        if (!response.ok) {
            throw new Error(payload.message || 'The character login request failed.');
        }
        return payload;
    };

    const restoreAuthentication = async () => {
        try {
            const session = await requestAuthenticationApi('/session');
            authenticatedAccount = session.authenticated ? session.account : null;
            authenticationCsrfToken = session.authenticated ? String(session.csrf_token || '') : '';
        } catch {
            authenticatedAccount = null;
            authenticationCsrfToken = '';
        }
        updateAuthenticationUi();
    };

    authButton?.addEventListener('click', () => {
        setAuthenticationMessage('');
        setAuthenticationMessage('', false, true);
        if (authDialog instanceof HTMLDialogElement) {
            authDialog.showModal();
            if (!authenticatedAccount) byId('auth-character-name')?.focus();
        }
    });

    byId('auth-dialog-close')?.addEventListener('click', () => {
        if (authDialog instanceof HTMLDialogElement) authDialog.close();
    });

    authLoginForm?.addEventListener('submit', async (event) => {
        event.preventDefault();
        const characterName = byId('auth-character-name');
        const password = byId('auth-password');
        const submit = byId('auth-submit');
        if (!(characterName instanceof HTMLInputElement)
            || !(password instanceof HTMLInputElement)
            || !(submit instanceof HTMLButtonElement)) {
            return;
        }

        submit.disabled = true;
        setAuthenticationMessage('Signing in…');
        try {
            const session = await requestAuthenticationApi('/login', {
                method: 'POST',
                body: {
                    character_name: characterName.value,
                    password: password.value
                }
            });
            authenticationCsrfToken = String(session.csrf_token || '');
            authenticatedAccount = session.account;
            try {
                const identity = await requestAuthenticationApi('/me');
                authenticatedAccount = identity.account || authenticatedAccount;
            } catch {
                // The login response is already bound to the same server session.
            }
            authLoginForm.reset();
            setAuthenticationMessage('');
            setAuthenticationMessage('Character login succeeded.', false, true);
            updateAuthenticationUi();
        } catch (error) {
            authenticatedAccount = null;
            authenticationCsrfToken = '';
            setAuthenticationMessage(error.message, true);
            updateAuthenticationUi();
        } finally {
            password.value = '';
            submit.disabled = false;
        }
    });

    byId('auth-logout')?.addEventListener('click', async () => {
        const logoutButton = byId('auth-logout');
        if (!(logoutButton instanceof HTMLButtonElement)) return;
        logoutButton.disabled = true;
        setAuthenticationMessage('Signing out…', false, true);
        try {
            await requestAuthenticationApi('/logout', { method: 'POST', csrf: true });
            authenticatedAccount = null;
            authenticationCsrfToken = '';
            updateAuthenticationUi();
            if (authDialog instanceof HTMLDialogElement) authDialog.close();
        } catch (error) {
            setAuthenticationMessage(error.message, true, true);
        } finally {
            logoutButton.disabled = false;
        }
    });

    updateAuthenticationUi();
    restoreAuthentication();

    const isStandalone = () => window.matchMedia('(display-mode: standalone)').matches
        || window.navigator.standalone === true;

    const installButtons = [byId('install-app'), byId('install-app-secondary')]
        .filter((button) => button instanceof HTMLButtonElement);

    const updateInstallButtons = () => {
        const installed = isStandalone();
        installButtons.forEach((button) => {
            button.disabled = installed;
            button.textContent = installed ? 'App installed' : 'Install app';
        });
        byId('install-card')?.toggleAttribute('hidden', installed);
    };

    window.addEventListener('beforeinstallprompt', (event) => {
        event.preventDefault();
        deferredInstallPrompt = event;
        updateInstallButtons();
    });

    window.addEventListener('appinstalled', () => {
        deferredInstallPrompt = null;
        updateInstallButtons();
    });

    const showInstallHelp = () => {
        const dialog = byId('install-help');
        const content = byId('install-help-content');
        if (!(dialog instanceof HTMLDialogElement) || content === null) {
            return;
        }

        const isIos = /iphone|ipad|ipod/i.test(navigator.userAgent);
        content.replaceChildren();
        const paragraph = document.createElement('p');
        paragraph.textContent = isIos
            ? 'Open the browser Share menu, then choose “Add to Home Screen.”'
            : 'Open your browser menu and choose “Install Player Assistant” or “Add to home screen.” Installation becomes available after the secure page and app manifest have loaded.';
        content.append(paragraph);
        dialog.showModal();
    };

    const requestInstall = async () => {
        if (isStandalone()) {
            return;
        }

        if (deferredInstallPrompt) {
            deferredInstallPrompt.prompt();
            await deferredInstallPrompt.userChoice;
            deferredInstallPrompt = null;
            updateInstallButtons();
            return;
        }

        showInstallHelp();
    };

    installButtons.forEach((button) => button.addEventListener('click', requestInstall));
    updateInstallButtons();

    if ('serviceWorker' in navigator) {
        window.addEventListener('load', async () => {
            try {
                const registration = await navigator.serviceWorker.register('service-worker.js', { scope: './' });
                await navigator.serviceWorker.ready;
                const offlineReadiness = byId('offline-readiness');
                if (offlineReadiness) {
                    offlineReadiness.textContent = registration.active
                        ? 'Offline app shell is ready.'
                        : 'Offline app shell is preparing.';
                }
            } catch {
                const offlineReadiness = byId('offline-readiness');
                if (offlineReadiness) {
                    offlineReadiness.textContent = 'Offline app shell could not be registered.';
                }
            }
        });
    }

    const input = byId('translator-input');
    const output = byId('translator-output');
    const languageSelect = byId('translator-language');
    const reverseToggle = byId('translator-reverse');
    const exportButton = byId('export-translation');
    const translationLoading = byId('translation-loading');
    const translationLoadingLabel = byId('translation-loading-label');

    const countWords = (text) => text.match(/[\p{L}\p{N}]+(?:['’\-][\p{L}\p{N}]+)*/gu)?.length ?? 0;

    const updateTranslationCounts = () => {
        const inputCount = byId('translator-input-count');
        const outputCount = byId('translator-output-count');
        const sourceWords = countWords(input?.value || '');
        const translatedWords = countWords(output?.value || '');
        if (inputCount) inputCount.textContent = `${sourceWords.toLocaleString()} ${sourceWords === 1 ? 'word' : 'words'}`;
        if (outputCount) outputCount.textContent = `${translatedWords.toLocaleString()} ${translatedWords === 1 ? 'word' : 'words'}`;
    };

    const updateTranslatorLabels = () => {
        const language = languageSelect?.value === 'elvish' ? 'Elvish' : 'Orcish';
        const reverse = reverseToggle?.checked === true;
        const reverseLabel = byId('reverse-label');
        const inputLabel = byId('translator-input-label');
        const outputLabel = byId('translator-output-label');
        if (reverseLabel) reverseLabel.textContent = `${language} to English`;
        if (inputLabel) inputLabel.textContent = reverse ? `${language} text` : 'English text';
        if (outputLabel) outputLabel.textContent = reverse ? 'English translation' : `${language} translation`;
        if (input instanceof HTMLTextAreaElement) {
            input.spellcheck = !reverse;
            input.placeholder = reverse ? `Enter ${language} text…` : 'Begin typing or paste text here…';
        }
        updateExportState();
    };

    const updateExportState = () => {
        if (!(exportButton instanceof HTMLButtonElement) || !(output instanceof HTMLTextAreaElement)) {
            return;
        }
        const allowed = reverseToggle?.checked !== true && output.value.trim().length > 0;
        exportButton.hidden = !allowed;
        exportButton.disabled = !allowed;
    };

    const setTranslationLoading = (loading, message = 'Preparing translation…') => {
        if (translationLoading) {
            translationLoading.hidden = !loading;
        }
        if (translationLoadingLabel) {
            translationLoadingLabel.textContent = message;
        }
    };

    const requestTranslation = (event) => {
        if (!(input instanceof HTMLTextAreaElement) || !(output instanceof HTMLTextAreaElement)) {
            return;
        }

        window.clearTimeout(translatorDebounce);
        const source = input.value;
        updateTranslationCounts();
        if (source.trim().length === 0) {
            output.value = '';
            setTranslationLoading(false);
            updateExportState();
            updateTranslationCounts();
            return;
        }

        const sourceWordCount = countWords(source);
        if (sourceWordCount > MAX_TRANSLATOR_WORDS) {
            output.value = `Please limit translation input to ${MAX_TRANSLATOR_WORDS.toLocaleString()} words.`;
            updateExportState();
            updateTranslationCounts();
            return;
        }

        const delay = event?.inputType === 'insertFromPaste' || source.length > 1200 ? 0 : 25;
        translatorDebounce = window.setTimeout(() => {
            const id = ++translatorRequestId;
            setTranslationLoading(true);
            if (worker) {
                worker.postMessage({
                    type: 'translate',
                    id,
                    language: languageSelect?.value === 'elvish' ? 'elvish' : 'orcish',
                    reverse: reverseToggle?.checked === true,
                    text: source
                });
            } else {
                output.value = 'This browser does not support background translation workers.';
                setTranslationLoading(false);
                updateExportState();
            }
        }, delay);
    };

    worker?.addEventListener('message', (event) => {
        const message = event.data || {};
        if (message.type === 'status') {
            const status = byId('lexicon-status');
            if (status) {
                status.lastElementChild.textContent = message.message;
            }
            if (message.loading) {
                setTranslationLoading(true, message.message);
            }
            return;
        }

        if (message.type !== 'translation' || message.id !== translatorRequestId) {
            return;
        }

        if (output instanceof HTMLTextAreaElement) {
            output.value = message.error ? `Translation unavailable: ${message.error}` : message.translation;
        }
        setTranslationLoading(false);
        updateTranslationCounts();
        updateExportState();
    });

    const resetTranslator = () => {
        window.clearTimeout(translatorDebounce);
        translatorRequestId++;
        if (input instanceof HTMLTextAreaElement) input.value = '';
        if (output instanceof HTMLTextAreaElement) output.value = '';
        setTranslationLoading(false);
        updateTranslatorLabels();
        updateTranslationCounts();
        input?.focus();
    };

    input?.addEventListener('input', requestTranslation);
    languageSelect?.addEventListener('change', () => {
        if (reverseToggle instanceof HTMLInputElement) reverseToggle.checked = false;
        resetTranslator();
        worker?.postMessage({ type: 'preload', language: languageSelect.value });
    });
    reverseToggle?.addEventListener('change', () => {
        resetTranslator();
    });

    exportButton?.addEventListener('click', () => {
        if (!(input instanceof HTMLTextAreaElement) || !(output instanceof HTMLTextAreaElement)) {
            return;
        }
        const languageToken = languageSelect?.value === 'elvish' ? 'elvish' : 'orcish';
        const sourceBytes = textEncoder.encode(input.value).length;
        const outputBytes = textEncoder.encode(output.value).length;
        const filename = `english-${sourceBytes}-bytes-to-${languageToken}-${outputBytes}-bytes.txt`;
        const url = URL.createObjectURL(new Blob([output.value], { type: 'text/plain;charset=utf-8' }));
        const link = document.createElement('a');
        link.href = url;
        link.download = filename;
        link.click();
        window.setTimeout(() => URL.revokeObjectURL(url), 1000);
    });

    updateTranslatorLabels();
    updateTranslationCounts();
    worker?.postMessage({ type: 'preload', language: 'orcish' });

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

    const searchWordCharacters = "\\p{L}\\p{N}'’-";
    const escapeRegularExpression = (value) => value.replace(/[.*+?^${}()|[\]\\]/gu, '\\$&');

    const createSearchExpression = (term) => {
        const leadingWildcard = term.startsWith('*');
        const trailingWildcard = term.endsWith('*');
        const core = term
            .split('*')
            .map(escapeRegularExpression)
            .join(`[${searchWordCharacters}]*`);
        if (!core) return null;
        const prefix = leadingWildcard ? '' : `(^|[^${searchWordCharacters}])`;
        const suffix = trailingWildcard ? '' : `(?=$|[^${searchWordCharacters}])`;
        return new RegExp(`${prefix}${core}${suffix}`, 'iu');
    };

    const matchesSearchTerm = (text, term) => createSearchExpression(term)?.test(text) === true;

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
                const combined = `${title} ${content}`;
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

    const secureRandomInt = (maximum) => {
        if (!Number.isSafeInteger(maximum) || maximum <= 0) throw new RangeError('Invalid random range.');
        const limit = Math.floor(0x100000000 / maximum) * maximum;
        const buffer = new Uint32Array(1);
        do {
            crypto.getRandomValues(buffer);
        } while (buffer[0] >= limit);
        return (buffer[0] % maximum) + 1;
    };

    const readDiceHistory = () => {
        try {
            const parsed = JSON.parse(localStorage.getItem('player-assistant.dice-history') || '[]');
            return Array.isArray(parsed) ? parsed.slice(0, 12) : [];
        } catch {
            return [];
        }
    };

    let diceHistory = readDiceHistory();

    const renderDiceHistory = () => {
        const list = byId('dice-history');
        if (!(list instanceof HTMLOListElement)) return;
        list.replaceChildren();
        if (diceHistory.length === 0) {
            const empty = document.createElement('li');
            empty.className = 'muted';
            empty.textContent = 'No rolls yet.';
            list.append(empty);
            return;
        }
        diceHistory.forEach((entry) => {
            const item = document.createElement('li');
            const expression = document.createElement('strong');
            expression.textContent = entry.expression;
            const total = document.createElement('strong');
            total.textContent = entry.total;
            const details = document.createElement('small');
            details.textContent = entry.details;
            item.append(expression, total, details);
            list.append(item);
        });
    };

    const rollDice = (rawExpression) => {
        const expression = rawExpression.replace(/\s+/g, '');
        const match = /^(\d*)d(\d+)([+-]\d+)?$/i.exec(expression);
        if (!match) throw new Error('Use a dice expression such as 1d20 or 2d6+3.');
        const count = Number.parseInt(match[1] || '1', 10);
        const sides = Number.parseInt(match[2], 10);
        const modifier = Number.parseInt(match[3] || '0', 10);
        if (count < 1 || count > 100) throw new Error('Roll between 1 and 100 dice.');
        if (sides < 2 || sides > 100000) throw new Error('Dice must have between 2 and 100,000 sides.');
        const rolls = Array.from({ length: count }, () => secureRandomInt(sides));
        const total = rolls.reduce((sum, value) => sum + value, 0) + modifier;
        const modifierText = modifier === 0 ? '' : ` ${modifier > 0 ? '+' : '−'} ${Math.abs(modifier)}`;
        return {
            expression: `${count}d${sides}${modifier === 0 ? '' : modifier > 0 ? `+${modifier}` : modifier}`,
            total: String(total),
            details: `[${rolls.join(', ')}]${modifierText}`
        };
    };

    const performDiceRoll = (expression) => {
        const result = byId('dice-result');
        if (!result) return;
        try {
            const roll = rollDice(expression);
            result.querySelector('span').textContent = roll.expression;
            result.querySelector('strong').textContent = roll.total;
            result.querySelector('small').textContent = roll.details;
            diceHistory.unshift(roll);
            diceHistory = diceHistory.slice(0, 12);
            localStorage.setItem('player-assistant.dice-history', JSON.stringify(diceHistory));
            renderDiceHistory();
        } catch (error) {
            result.querySelector('span').textContent = 'Unable to roll';
            result.querySelector('strong').textContent = '—';
            result.querySelector('small').textContent = error.message;
        }
    };

    byId('dice-form')?.addEventListener('submit', (event) => {
        event.preventDefault();
        performDiceRoll(byId('dice-expression')?.value || '');
    });
    document.querySelectorAll('[data-die]').forEach((button) => {
        button.addEventListener('click', () => {
            const expressionInput = byId('dice-expression');
            if (expressionInput instanceof HTMLInputElement) expressionInput.value = button.dataset.die || '1d20';
            performDiceRoll(button.dataset.die || '1d20');
        });
    });
    byId('clear-dice-history')?.addEventListener('click', () => {
        diceHistory = [];
        localStorage.removeItem('player-assistant.dice-history');
        renderDiceHistory();
    });
    renderDiceHistory();

    setView(location.hash.slice(1) || 'dashboard', false);
    console.info(`${APP_NAME} ${APP_VERSION} initialized.`);
})();
