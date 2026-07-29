(() => {
    'use strict';

    const APP_NAME = 'Player Assistant';
    const APP_VERSION = '0.9.8';
    const AUTH_API_ROOT = '/scarlethorizons/api/v1';
    const MAGIC_ITEMS_WIKI_URL = 'https://publish.obsidian.md/scarlethorizons/Magic+Items/Kirkilston+Crew+Magic+Items';
    const MAGIC_ITEMS_MARKDOWN_ROOT = 'https://publish-01.obsidian.md/access/1113217a28a5bfdcc9fbe8e6d82b27ac/Magic%20Items/';
    const MAGIC_ITEM_LONGEVITY_VALUES = Object.freeze(['one-shot', 'limited-use', 'permanent']);
    const MAX_SEARCH_RESULTS = 40;
    const MAX_TRANSLATOR_WORDS = 5000;
    const textEncoder = new TextEncoder();
    const DUNGEON_MASTER_HERO = Object.freeze({
        name: 'Dungeon Master',
        aliases: ['Dungeon Master'],
        token: 'data/hero-tokens/dungeon-master-914c56786be2.webp',
        preferLocal: true
    });
    const QUEST_STATE_VALUES = Object.freeze([
        'available',
        'active',
        'available (abandoned)',
        'completed',
        'withdrawn'
    ]);
    const QUEST_STATE_DISPLAY_ORDER = Object.freeze([
        'active',
        'available',
        'available (abandoned)',
        'completed',
        'withdrawn'
    ]);
    const QUEST_STATUS_VALUES = Object.freeze([
        'individual-only',
        'party-only',
        'individual-or-party',
        ...QUEST_STATE_VALUES
    ]);
    const QUEST_REQUEST_STATUS_VALUES = Object.freeze([
        'pending',
        'approved',
        'denied'
    ]);
    const QUEST_STATUS_LABELS = Object.freeze({
        'individual-only': 'Individual-Only',
        'party-only': 'Party-Only',
        'individual-or-party': 'Individual-Or-Party',
        available: 'Available',
        active: 'Active',
        'available (abandoned)': 'Available (Abandoned)',
        completed: 'Completed',
        withdrawn: 'Withdrawn'
    });

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
    let authenticatedXpSnapshot = null;
    let xpRequestId = 0;
    let authenticatedWordCountSnapshot = null;
    let wordCountRequestId = 0;
    let authenticatedPresenceSnapshot = null;
    let presenceRequestId = 0;
    let presencePollTimer = 0;
    let authenticatedQuestSnapshot = null;
    let questRequestId = 0;
    let questStateFilter = '';
    let lastQuestAlertSignature = '';
    let magicItemSnapshot = null;
    let magicItemLoading = null;
    let magicItemError = '';
    let heroTokenData = null;
    let heroTokenDataLoading = null;

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
        if (resolvedView === 'quests'
            && authenticatedAccount !== null
            && authenticatedQuestSnapshot === null) {
            void loadQuests();
        }
        if (resolvedView === 'magic-items' && magicItemSnapshot === null) {
            void loadMagicItems();
        }

        byId('main-content')?.focus({ preventScroll: true });
        window.scrollTo({ top: 0, behavior: 'smooth' });
        document.title = resolvedView === 'dashboard'
            ? APP_NAME
            : `${resolvedView[0].toUpperCase()}${resolvedView.slice(1)} · ${APP_NAME}`;
    };

    navButtons.forEach((button) => {
        button.addEventListener('click', () => {
            const viewName = button.dataset.view || 'dashboard';
            if (viewName === 'quests') questStateFilter = '';
            setView(viewName);
            if (viewName === 'quests') renderQuestUi();
        });
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

    const renderXpUi = () => {
        const authenticated = authenticatedAccount !== null;
        const refreshButton = byId('xp-refresh');
        const status = byId('xp-status');
        const characterSummary = byId('xp-character-summary');
        const partySummary = byId('xp-party-summary');
        const characterName = byId('xp-character-name');
        const xpTotal = byId('xp-total');
        const xpDate = byId('xp-date');
        const classLevel = byId('xp-class-level');
        const hitPoints = byId('xp-hit-points');
        const tnl = byId('xp-tnl');
        const partyDate = byId('xp-party-date');
        const partyRows = byId('xp-party-rows');
        if (refreshButton) refreshButton.hidden = !authenticated;
        if (characterSummary) characterSummary.hidden = true;
        if (partySummary) partySummary.hidden = true;
        if (characterName) characterName.textContent = '';
        if (xpTotal) xpTotal.textContent = '';
        if (xpDate) xpDate.textContent = '';
        if (classLevel) classLevel.textContent = '';
        if (hitPoints) hitPoints.textContent = '';
        if (tnl) tnl.textContent = '';
        if (partyDate) partyDate.textContent = '';
        partyRows?.replaceChildren();

        if (!authenticated) {
            if (status) status.textContent = 'Log in to view your current XP total.';
            return;
        }
        if (authenticatedXpSnapshot === null) {
            if (status) status.textContent = 'Loading current XP…';
            return;
        }

        if (authenticatedXpSnapshot.scope === 'character') {
            const character = authenticatedXpSnapshot.character;
            if (status) status.textContent = authenticatedXpSnapshot.stale
                ? 'The XP source is temporarily unavailable. Showing the last validated total.'
                : '';
            if (characterSummary) characterSummary.hidden = false;
            if (characterName) characterName.textContent = character.character_name;
            const tnlLabel = character.xp_to_next_level === null
                ? 'Max level'
                : Number(character.xp_to_next_level).toLocaleString('en-US');
            if (xpTotal) {
                xpTotal.textContent = `${Number(character.xp_total).toLocaleString('en-US')} (TNL: ${tnlLabel})`;
            }
            if (xpDate) xpDate.textContent = authenticatedXpSnapshot.date_label;
            if (classLevel) classLevel.textContent = `${character.character_class} ${character.level}`;
            if (hitPoints) hitPoints.textContent = Number(character.hit_points).toLocaleString('en-US');
            if (tnl) tnl.textContent = tnlLabel;
            return;
        }

        if (status) status.textContent = authenticatedXpSnapshot.stale
            ? 'The XP source is temporarily unavailable. Showing the last validated party totals.'
            : 'Dungeon Master party access';
        if (partySummary) partySummary.hidden = false;
        if (partyDate) partyDate.textContent = authenticatedXpSnapshot.date_label;
        if (partyRows) {
            const fragment = document.createDocumentFragment();
            authenticatedXpSnapshot.characters.forEach((character) => {
                const row = document.createElement('tr');
                const nameCell = document.createElement('th');
                const totalCell = document.createElement('td');
                nameCell.scope = 'row';
                nameCell.textContent = character.character_name;
                const tnlLabel = character.xp_to_next_level === null
                    ? 'Max level'
                    : Number(character.xp_to_next_level).toLocaleString('en-US');
                totalCell.textContent = `${Number(character.xp_total).toLocaleString('en-US')} (TNL: ${tnlLabel})`;
                row.append(nameCell, totalCell);
                fragment.append(row);
            });
            partyRows.replaceChildren(fragment);
        }
    };

    const validateXpSnapshot = (payload) => {
        if (!payload
            || payload.schema_version !== 1
            || typeof payload.date_label !== 'string'
            || payload.date_label.length === 0
            || payload.date_label.length > 200
            || typeof payload.stale !== 'boolean') {
            throw new Error('The XP service returned an invalid response.');
        }
        const validCharacter = (character) => character
            && typeof character.character_name === 'string'
            && character.character_name.length > 0
            && character.character_name.length <= 100
            && typeof character.character_class === 'string'
            && character.character_class.length > 0
            && character.character_class.length <= 100
            && Number.isSafeInteger(character.level)
            && character.level >= 0
            && character.level <= 1000
            && Number.isSafeInteger(character.hit_points)
            && character.hit_points >= 0
            && character.hit_points <= 1000000
            && Number.isSafeInteger(character.xp_total)
            && character.xp_total >= 0
            && (character.xp_to_next_level === null
                || (Number.isSafeInteger(character.xp_to_next_level)
                    && character.xp_to_next_level >= 0));
        if (payload.scope === 'character' && validCharacter(payload.character)) {
            return payload;
        }
        if (payload.scope === 'party'
            && Array.isArray(payload.characters)
            && payload.characters.length > 0
            && payload.characters.length <= 200
            && payload.characters.every(validCharacter)) {
            return payload;
        }
        throw new Error('The XP service returned an invalid response.');
    };

    const loadXpSummary = async () => {
        const requestId = ++xpRequestId;
        if (authenticatedAccount === null) {
            authenticatedXpSnapshot = null;
            renderXpUi();
            return;
        }
        const accountId = authenticatedAccount.id;
        const refreshButton = byId('xp-refresh');
        if (refreshButton instanceof HTMLButtonElement) refreshButton.disabled = true;
        authenticatedXpSnapshot = null;
        renderXpUi();
        try {
            const snapshot = validateXpSnapshot(await requestAuthenticationApi('/xp'));
            if (requestId !== xpRequestId || authenticatedAccount?.id !== accountId) return;
            authenticatedXpSnapshot = snapshot;
            renderXpUi();
        } catch (error) {
            if (requestId !== xpRequestId || authenticatedAccount?.id !== accountId) return;
            const status = byId('xp-status');
            if (status) status.textContent = error.message;
        } finally {
            if (requestId === xpRequestId && refreshButton instanceof HTMLButtonElement) {
                refreshButton.disabled = false;
            }
        }
    };

    const renderWordCountUi = () => {
        const authenticated = authenticatedAccount !== null;
        const refreshButton = byId('word-count-refresh');
        const status = byId('word-count-status');
        const summary = byId('word-count-summary');
        if (refreshButton) refreshButton.hidden = !authenticated;
        if (summary) summary.hidden = true;
        for (const id of [
            'word-count-wiki',
            'word-count-wiki-pages',
            'word-count-ic',
            'word-count-ic-files',
            'word-count-ooc',
            'word-count-ooc-files',
            'word-count-date'
        ]) {
            const element = byId(id);
            if (element) element.textContent = '';
        }

        if (!authenticated) {
            if (status) status.textContent = 'Log in to view the latest campaign word counts.';
            return;
        }
        if (authenticatedWordCountSnapshot === null) {
            if (status) status.textContent = 'Loading campaign word counts…';
            return;
        }

        const snapshot = authenticatedWordCountSnapshot;
        if (status) status.textContent = '';
        if (summary) summary.hidden = false;
        byId('word-count-wiki').textContent = Number(snapshot.wiki.words).toLocaleString('en-US');
        byId('word-count-wiki-pages').textContent = `${Number(snapshot.wiki.pages).toLocaleString('en-US')} pages`;
        byId('word-count-ic').textContent = Number(snapshot.ic.words).toLocaleString('en-US');
        byId('word-count-ic-files').textContent = `${Number(snapshot.ic.files).toLocaleString('en-US')} files`;
        byId('word-count-ooc').textContent = Number(snapshot.ooc.words).toLocaleString('en-US');
        byId('word-count-ooc-files').textContent = `${Number(snapshot.ooc.files).toLocaleString('en-US')} files`;
        const observed = new Date(snapshot.observed_at);
        byId('word-count-date').textContent = `Counted ${observed.toLocaleString('en-US')}`;
    };

    const validateWordCountSnapshot = (payload) => {
        const validGroup = (group, unitKey) => group
            && Number.isSafeInteger(group[unitKey])
            && group[unitKey] > 0
            && Number.isSafeInteger(group.words)
            && group.words >= 0;
        if (!payload
            || payload.schema_version !== 1
            || typeof payload.observed_at !== 'string'
            || Number.isNaN(Date.parse(payload.observed_at))
            || typeof payload.counting_rule_version !== 'string'
            || payload.counting_rule_version.length < 1
            || payload.counting_rule_version.length > 100
            || !validGroup(payload.wiki, 'pages')
            || !validGroup(payload.ic, 'files')
            || !validGroup(payload.ooc, 'files')) {
            throw new Error('The word-count service returned an invalid response.');
        }
        return payload;
    };

    const loadWordCountSummary = async () => {
        const requestId = ++wordCountRequestId;
        if (authenticatedAccount === null) {
            authenticatedWordCountSnapshot = null;
            renderWordCountUi();
            return;
        }
        const accountId = authenticatedAccount.id;
        const refreshButton = byId('word-count-refresh');
        if (refreshButton instanceof HTMLButtonElement) refreshButton.disabled = true;
        authenticatedWordCountSnapshot = null;
        renderWordCountUi();
        try {
            const snapshot = validateWordCountSnapshot(
                await requestAuthenticationApi('/word-counts'));
            if (requestId !== wordCountRequestId || authenticatedAccount?.id !== accountId) return;
            authenticatedWordCountSnapshot = snapshot;
            renderWordCountUi();
        } catch (error) {
            if (requestId !== wordCountRequestId || authenticatedAccount?.id !== accountId) return;
            const status = byId('word-count-status');
            if (status) status.textContent = error.message;
        } finally {
            if (requestId === wordCountRequestId && refreshButton instanceof HTMLButtonElement) {
                refreshButton.disabled = false;
            }
        }
    };

    const validateQuestSnapshot = (payload) => {
        const validShortText = (value, maximum = 200) =>
            typeof value === 'string' && value.length <= maximum;
        const validRequiredText = (value, maximum = 500) =>
            validShortText(value, maximum) && value.trim().length > 0;
        const validRequest = (request) => request
            && typeof request.id === 'string'
            && /^[a-f0-9]{32}$/u.test(request.id)
            && typeof request.quest_id === 'string'
            && /^[a-z0-9]+(?:-[a-z0-9]+)*$/u.test(request.quest_id)
            && validRequiredText(request.quest_title, 200)
            && validRequiredText(request.requester_character_name, 100)
            && QUEST_REQUEST_STATUS_VALUES.includes(request.status)
            && typeof request.created_at === 'string'
            && !Number.isNaN(Date.parse(request.created_at))
            && (request.decided_at === null
                || (typeof request.decided_at === 'string'
                    && !Number.isNaN(Date.parse(request.decided_at))))
            && (request.status === 'pending'
                ? request.decided_at === null
                : request.decided_at !== null);
        const validQuest = (quest) => quest
            && /^[a-z0-9]+(?:-[a-z0-9]+)*$/u.test(quest.id)
            && validRequiredText(quest.title, 200)
            && validRequiredText(quest.summary, 1000)
            && validRequiredText(quest.quest_giver, 200)
            && ['individual-only', 'party-only', 'individual-or-party'].includes(quest.visibility)
            && ['available', 'active', 'available (abandoned)', 'completed', 'withdrawn'].includes(quest.state)
            && Array.isArray(quest.objectives)
            && quest.objectives.length > 0
            && quest.objectives.length <= 20
            && quest.objectives.every((objective) => validRequiredText(objective, 500))
            && validShortText(quest.reward, 500)
            && validShortText(quest.accepted_on, 100)
            && validShortText(quest.expires_on, 100)
            && (quest.request_status === null
                || QUEST_REQUEST_STATUS_VALUES.includes(quest.request_status))
            && /^https:\/\/publish\.obsidian\.md\/scarlethorizons\/(?:Quests|NPCs|Meta\/IC|Writings)\/[^?#]+$/u.test(quest.wiki_url);
        if (!payload
            || payload.schema_version !== 2
            || !Array.isArray(payload.status_values)
            || payload.status_values.length !== QUEST_STATUS_VALUES.length
            || !QUEST_STATUS_VALUES.every((status) => payload.status_values.includes(status))
            || !Array.isArray(payload.request_status_values)
            || payload.request_status_values.length !== QUEST_REQUEST_STATUS_VALUES.length
            || !QUEST_REQUEST_STATUS_VALUES.every(
                (status) => payload.request_status_values.includes(status))
            || !Array.isArray(payload.quests)
            || payload.quests.length > 100
            || !payload.quests.every(validQuest)
            || !Array.isArray(payload.pending_requests)
            || payload.pending_requests.length > 200
            || !payload.pending_requests.every(validRequest)
            || !Array.isArray(payload.notifications)
            || payload.notifications.length > 200
            || !payload.notifications.every(validRequest)
            || (authenticatedAccount?.role === 'dm'
                && (payload.notifications.length !== 0
                    || payload.quests.some((quest) => quest.request_status !== null)))
            || (authenticatedAccount?.role === 'player'
                && payload.pending_requests.length !== 0)) {
            throw new Error('The quest service returned an invalid response.');
        }
        return payload;
    };

    const appendQuestDetail = (list, label, value) => {
        if (!(list instanceof HTMLDListElement) || value === '') return;
        const wrapper = document.createElement('div');
        const term = document.createElement('dt');
        const detail = document.createElement('dd');
        term.textContent = label;
        detail.textContent = value;
        wrapper.append(term, detail);
        list.append(wrapper);
    };

    const submitQuestInterest = async (questId, button) => {
        if (!(button instanceof HTMLButtonElement)
            || authenticatedAccount?.role !== 'player') {
            return;
        }
        button.disabled = true;
        const status = byId('quests-status');
        if (status) status.textContent = 'Sending quest request…';
        try {
            await requestAuthenticationApi('/quest-requests', {
                method: 'POST',
                body: { quest_id: questId },
                csrf: true
            });
            await loadQuests();
        } catch (error) {
            if (status) status.textContent = error.message;
            button.disabled = false;
        }
    };

    const decideQuestRequest = async (requestId, decision, button) => {
        if (!(button instanceof HTMLButtonElement)
            || authenticatedAccount?.role !== 'dm'
            || !['approved', 'denied'].includes(decision)) {
            return;
        }
        button.disabled = true;
        const summary = byId('quest-alert-summary');
        if (summary) summary.textContent = decision === 'approved'
            ? 'Activating quest and recording approval…'
            : 'Recording denial…';
        try {
            await requestAuthenticationApi(
                `/quest-requests/${requestId}/decision`,
                {
                    method: 'POST',
                    body: { decision },
                    csrf: true
                });
            await loadQuests();
        } catch (error) {
            if (summary) summary.textContent = error.message;
            button.disabled = false;
        }
    };

    const acknowledgeQuestNotification = async (requestId, button) => {
        if (!(button instanceof HTMLButtonElement)
            || authenticatedAccount?.role !== 'player') {
            return;
        }
        button.disabled = true;
        const summary = byId('quest-alert-summary');
        if (summary) summary.textContent = 'Dismissing quest notification…';
        try {
            await requestAuthenticationApi(
                `/quest-requests/${requestId}/acknowledge`,
                { method: 'POST', body: {}, csrf: true });
            await loadQuests();
        } catch (error) {
            if (summary) summary.textContent = error.message;
            button.disabled = false;
        }
    };

    const renderQuestAlerts = () => {
        const dialog = byId('quest-alert-dialog');
        const summary = byId('quest-alert-summary');
        const list = byId('quest-alert-list');
        if (!(dialog instanceof HTMLDialogElement) || list === null) return;

        const alerts = authenticatedAccount?.role === 'dm'
            ? (authenticatedQuestSnapshot?.pending_requests || [])
            : (authenticatedQuestSnapshot?.notifications || []);
        list.replaceChildren();
        if (alerts.length === 0) {
            if (dialog.open) dialog.close();
            lastQuestAlertSignature = '';
            return;
        }

        if (summary) {
            summary.textContent = authenticatedAccount?.role === 'dm'
                ? `${alerts.length} PC quest request${alerts.length === 1 ? '' : 's'} await your decision.`
                : `${alerts.length} quest request decision${alerts.length === 1 ? '' : 's'} received.`;
        }
        const fragment = document.createDocumentFragment();
        alerts.forEach((request) => {
            const alert = document.createElement('article');
            alert.className = 'quest-alert';
            const heading = document.createElement('h3');
            const message = document.createElement('p');
            const actions = document.createElement('div');
            actions.className = 'quest-alert-actions';

            if (authenticatedAccount?.role === 'dm') {
                heading.textContent = request.quest_title;
                message.textContent =
                    `${request.requester_character_name} would like to take this quest.`;
                const approve = document.createElement('button');
                approve.className = 'primary-button';
                approve.type = 'button';
                approve.textContent = 'Approve';
                approve.addEventListener('click', () => {
                    void decideQuestRequest(request.id, 'approved', approve);
                });
                const deny = document.createElement('button');
                deny.className = 'secondary-button';
                deny.type = 'button';
                deny.textContent = 'Deny';
                deny.addEventListener('click', () => {
                    void decideQuestRequest(request.id, 'denied', deny);
                });
                actions.append(approve, deny);
            } else {
                heading.textContent = request.quest_title;
                message.textContent = request.status === 'approved'
                    ? 'The Dungeon Master approved your request. This quest is now active.'
                    : 'The Dungeon Master denied your request.';
                const dismiss = document.createElement('button');
                dismiss.className = 'secondary-button';
                dismiss.type = 'button';
                dismiss.textContent = 'Dismiss';
                dismiss.addEventListener('click', () => {
                    void acknowledgeQuestNotification(request.id, dismiss);
                });
                actions.append(dismiss);
            }
            alert.append(heading, message, actions);
            fragment.append(alert);
        });
        list.append(fragment);

        const signature = alerts
            .map((request) => `${request.id}:${request.status}`)
            .join('|');
        if (!dialog.open && signature !== lastQuestAlertSignature) {
            lastQuestAlertSignature = signature;
            dialog.showModal();
        }
    };

    const renderQuestUi = () => {
        const status = byId('quests-status');
        const list = byId('quest-list');
        const stateCycle = byId('quest-state-cycle');
        const stateCycleLabel = byId('quest-state-cycle-label');
        list?.replaceChildren();
        if (list) list.hidden = true;
        if (stateCycle) stateCycle.hidden = true;

        if (authenticatedAccount === null) {
            questStateFilter = '';
            if (status) status.textContent = 'Log in as your character to view available quests.';
            return;
        }
        if (authenticatedQuestSnapshot === null) {
            if (status) status.textContent = 'Loading quests…';
            return;
        }
        renderQuestAlerts();
        if (authenticatedQuestSnapshot.quests.length === 0) {
            if (status) status.textContent = 'No quests are currently visible to this character.';
            return;
        }

        const availableStates = QUEST_STATE_DISPLAY_ORDER.filter((state) =>
            authenticatedQuestSnapshot.quests.some((quest) => quest.state === state));
        if (!availableStates.includes(questStateFilter)) questStateFilter = '';
        if (stateCycle && stateCycleLabel && availableStates.length > 1) {
            const filterLabel = questStateFilter === ''
                ? 'All states'
                : QUEST_STATUS_LABELS[questStateFilter];
            stateCycleLabel.textContent = filterLabel;
            stateCycle.setAttribute(
                'aria-label',
                `Cycle quest state filter. Currently showing ${filterLabel}.`);
            stateCycle.title = `Currently showing ${filterLabel}`;
            stateCycle.hidden = false;
        }

        const orderedQuests = authenticatedQuestSnapshot.quests
            .map((quest, sourceIndex) => ({ quest, sourceIndex }))
            .sort((left, right) =>
                QUEST_STATE_DISPLAY_ORDER.indexOf(left.quest.state)
                    - QUEST_STATE_DISPLAY_ORDER.indexOf(right.quest.state)
                || left.sourceIndex - right.sourceIndex)
            .map(({ quest }) => quest);
        const visibleQuests = questStateFilter === ''
            ? orderedQuests
            : orderedQuests.filter((quest) => quest.state === questStateFilter);

        if (status) {
            status.textContent = questStateFilter === ''
                ? ''
                : `Showing ${visibleQuests.length} ${QUEST_STATUS_LABELS[questStateFilter]} quest${visibleQuests.length === 1 ? '' : 's'}.`;
        }
        if (!list) return;
        const fragment = document.createDocumentFragment();
        visibleQuests.forEach((quest) => {
            const card = document.createElement('article');
            card.className = 'quest-card';

            const heading = document.createElement('header');
            heading.className = 'quest-card-heading';
            const title = document.createElement('h2');
            title.textContent = quest.title;
            const tags = document.createElement('div');
            tags.className = 'quest-tags';
            for (const tagValue of [quest.state, quest.visibility]) {
                const tag = document.createElement('span');
                const tagClass = tagValue.replace(/[^a-z0-9]+/gu, '-').replace(/^-|-$/gu, '');
                tag.className = `quest-tag quest-tag-${tagClass}`;
                tag.textContent = QUEST_STATUS_LABELS[tagValue];
                tags.append(tag);
            }
            heading.append(title, tags);

            const summary = document.createElement('p');
            summary.className = 'quest-summary';
            summary.textContent = quest.summary;

            const details = document.createElement('dl');
            details.className = 'quest-details';
            appendQuestDetail(details, 'Quest giver', quest.quest_giver);
            appendQuestDetail(details, 'Accepted', quest.accepted_on);
            appendQuestDetail(details, 'Deadline', quest.expires_on);
            appendQuestDetail(details, 'Reward', quest.reward);

            const objectivesTitle = document.createElement('h3');
            objectivesTitle.textContent = 'Objectives';
            const objectives = document.createElement('ul');
            objectives.className = 'quest-objectives';
            quest.objectives.forEach((objective) => {
                const item = document.createElement('li');
                item.textContent = objective;
                objectives.append(item);
            });

            const wikiLink = document.createElement('a');
            wikiLink.className = 'quest-wiki-link';
            wikiLink.href = quest.wiki_url;
            wikiLink.target = '_blank';
            wikiLink.rel = 'noopener noreferrer';
            wikiLink.textContent = 'Open quest on the campaign wiki';

            card.append(heading, summary, details, objectivesTitle, objectives);
            if (authenticatedAccount?.role === 'player'
                && ['available', 'available (abandoned)'].includes(quest.state)) {
                const requestActions = document.createElement('div');
                requestActions.className = 'quest-request-actions';
                if (quest.request_status === 'pending') {
                    const pending = document.createElement('span');
                    pending.className = 'quest-request-pending';
                    pending.textContent = 'Quest request pending';
                    requestActions.append(pending);
                } else {
                    const requestButton = document.createElement('button');
                    requestButton.className = 'primary-button';
                    requestButton.type = 'button';
                    requestButton.textContent = quest.request_status === 'denied'
                        ? 'Request again'
                        : 'Request this quest';
                    requestButton.addEventListener('click', () => {
                        void submitQuestInterest(quest.id, requestButton);
                    });
                    requestActions.append(requestButton);
                }
                card.append(requestActions);
            }
            card.append(wikiLink);
            fragment.append(card);
        });
        list.append(fragment);
        list.hidden = false;
    };

    const loadQuests = async () => {
        const requestId = ++questRequestId;
        if (authenticatedAccount === null) {
            authenticatedQuestSnapshot = null;
            renderQuestUi();
            return;
        }
        const accountId = authenticatedAccount.id;
        authenticatedQuestSnapshot = null;
        renderQuestUi();
        try {
            const snapshot = validateQuestSnapshot(
                await requestAuthenticationApi('/quests'));
            if (requestId !== questRequestId || authenticatedAccount?.id !== accountId) return;
            authenticatedQuestSnapshot = snapshot;
            renderQuestUi();
        } catch (error) {
            if (requestId !== questRequestId || authenticatedAccount?.id !== accountId) return;
            const status = byId('quests-status');
            if (status) status.textContent = error.message;
        }
    };

    const validMagicItemText = (value, maximum = 4000) =>
        typeof value === 'string' && value.trim().length > 0 && value.length <= maximum;

    const getMagicItemViewers = (value) => String(value || 'all')
        .split(',')
        .map((viewer) => viewer.trim().toLowerCase())
        .filter((viewer) => viewer !== '');

    const isMagicItemVisible = (item) => {
        const viewableBy = String(item?.['viewable-by'] || '').toLocaleLowerCase('en-US');
        if (getMagicItemViewers(viewableBy).includes('all')) return true;
        if (authenticatedAccount === null) return false;
        const characterNames = [
            authenticatedAccount.character_name,
            authenticatedAccount.character_key,
            String(authenticatedAccount.character_name || '').split(/\s+/u)[0]
        ]
            .map((name) => String(name || '').normalize('NFKC').trim().toLocaleLowerCase('en-US'))
            .filter((name) => name !== '');
        return characterNames.some((name) => viewableBy.includes(name));
    };

    const validateMagicItems = (payload) => {
        const validItem = (item) => item
            && validMagicItemText(item.name, 200)
            && validMagicItemText(item.description, 10000)
            && validMagicItemText(item['date-acquired'], 200)
            && validMagicItemText(item['meta-date-acquired'], 100)
            && MAGIC_ITEM_LONGEVITY_VALUES.includes(item.longevity)
            && validMagicItemText(item.provenance, 1000)
            && validMagicItemText(item.whereabouts, 500)
            && validMagicItemText(item['viewable-by'], 500)
            && getMagicItemViewers(item['viewable-by']).length > 0;
        if (!payload
            || payload.schema_version !== 1
            || payload.source !== MAGIC_ITEMS_WIKI_URL
            || !Array.isArray(payload.items)
            || payload.items.length > 100
            || !payload.items.every(validItem)) {
            throw new Error('Magic-item data is invalid.');
        }
        return payload;
    };

    const parseMarkdownFrontmatter = (markdown) => {
        const normalized = String(markdown || '').replace(/\r\n?/gu, '\n');
        const match = /^---\s*\n([\s\S]*?)\n---\s*\n?([\s\S]*)$/u.exec(normalized);
        if (!match) throw new Error('A wiki magic-item page has no frontmatter.');
        const metadata = {};
        for (const line of match[1].split('\n')) {
            const property = /^([a-z0-9-]+):\s*(.*?)\s*$/iu.exec(line);
            if (!property) continue;
            let value = property[2];
            if ((value.startsWith('"') && value.endsWith('"'))
                || (value.startsWith("'") && value.endsWith("'"))) {
                value = value.slice(1, -1);
            }
            metadata[property[1].toLowerCase()] = value;
        }
        const description = match[2]
            .replace(/\[\[([^\]|]+)\|([^\]]+)\]\]/gu, '$2')
            .replace(/\[\[([^\]]+)\]\]/gu, '$1')
            .replace(/[_*]+/gu, '')
            .trim();
        return { metadata, description };
    };

    const getMagicItemMarkdownUrl = (pageName) => {
        if (!/^[^/\\?#]{1,200}$/u.test(pageName)) {
            throw new Error('The wiki magic-item index contains an invalid link.');
        }
        return `${MAGIC_ITEMS_MARKDOWN_ROOT}${encodeURIComponent(pageName)}.md`;
    };

    const fetchWikiMagicItems = async () => {
        const indexResponse = await fetch(
            `${MAGIC_ITEMS_MARKDOWN_ROOT}Kirkilston%20Crew%20Magic%20Items.md`,
            { cache: 'no-store', mode: 'cors', credentials: 'omit' });
        if (!indexResponse.ok) throw new Error('The magic-item wiki index is unavailable.');
        const indexMarkdown = await indexResponse.text();
        const pageNames = [...indexMarkdown.matchAll(/\[\[([^\]|#]+)(?:#[^\]|]*)?(?:\|[^\]]+)?\]\]/gu)]
            .map((match) => match[1].trim())
            .filter((name, index, names) => name !== '' && names.indexOf(name) === index);
        if (pageNames.length === 0 || pageNames.length > 100) {
            throw new Error('The magic-item wiki index contains no usable item links.');
        }
        const items = await Promise.all(pageNames.map(async (pageName) => {
            const response = await fetch(
                getMagicItemMarkdownUrl(pageName),
                { cache: 'no-store', mode: 'cors', credentials: 'omit' });
            if (!response.ok) throw new Error(`The wiki page for ${pageName} is unavailable.`);
            const { metadata, description } = parseMarkdownFrontmatter(await response.text());
            return {
                name: metadata.name || pageName,
                description,
                'date-acquired': metadata['date-acquired'] || '',
                'meta-date-acquired': metadata['meta-date-acquired'] || '',
                longevity: metadata.longevity || '',
                provenance: metadata.provenance || '',
                whereabouts: metadata.whereabouts || '',
                'viewable-by': metadata['viewable-by'] || 'all'
            };
        }));
        return validateMagicItems({
            schema_version: 1,
            source: MAGIC_ITEMS_WIKI_URL,
            items
        });
    };

    const fetchFallbackMagicItems = async () => {
        const response = await fetch('magic-items.json', { cache: 'no-cache' });
        if (!response.ok) throw new Error('The bundled magic-item fallback is unavailable.');
        return validateMagicItems(await response.json());
    };

    const appendMagicItemDetail = (list, label, value) => {
        const wrapper = document.createElement('div');
        const term = document.createElement('dt');
        const detail = document.createElement('dd');
        term.textContent = label;
        detail.textContent = value;
        wrapper.append(term, detail);
        list.append(wrapper);
    };

    const renderMagicItems = () => {
        const status = byId('magic-items-status');
        const list = byId('magic-item-list');
        const counts = byId('magic-item-counts');
        list?.replaceChildren();
        if (list) list.hidden = true;
        if (counts) counts.hidden = true;
        if (magicItemSnapshot === null) {
            if (status) {
                status.textContent = magicItemLoading
                    ? 'Loading magic items…'
                    : (magicItemError || 'Magic items load when this page is opened.');
            }
            return;
        }
        if (status) {
            status.textContent = magicItemSnapshot.data_source === 'wiki'
                ? 'Current information loaded from the campaign wiki.'
                : 'The campaign wiki is unavailable; showing the bundled offline fallback.';
        }
        if (!list) return;
        const visibleItems = magicItemSnapshot.items.filter(isMagicItemVisible);
        const longevityCounts = Object.fromEntries(
            MAGIC_ITEM_LONGEVITY_VALUES.map((longevity) => [
                longevity,
                visibleItems.filter((item) => item.longevity === longevity).length
            ]));
        MAGIC_ITEM_LONGEVITY_VALUES.forEach((longevity) => {
            const count = byId(`magic-item-count-${longevity}`);
            if (count) count.textContent = String(longevityCounts[longevity]);
        });
        if (counts) counts.hidden = false;
        if (visibleItems.length === 0) {
            if (status) status.textContent = 'No magic items are currently visible to this character.';
            return;
        }
        const fragment = document.createDocumentFragment();
        visibleItems.forEach((item) => {
            const card = document.createElement('article');
            card.className = 'magic-item-card';
            const heading = document.createElement('header');
            heading.className = 'magic-item-card-heading';
            const title = document.createElement('h2');
            title.textContent = item.name;
            const longevity = document.createElement('span');
            longevity.className = 'magic-item-longevity';
            longevity.textContent = item.longevity;
            heading.append(title, longevity);
            const description = document.createElement('p');
            description.className = 'magic-item-description';
            description.textContent = item.description;
            const details = document.createElement('dl');
            details.className = 'magic-item-details';
            appendMagicItemDetail(details, 'Acquired', item['date-acquired']);
            appendMagicItemDetail(details, 'Real-world date', item['meta-date-acquired']);
            appendMagicItemDetail(details, 'Provenance', item.provenance);
            appendMagicItemDetail(details, 'Whereabouts', item.whereabouts);
            card.append(heading, description, details);
            fragment.append(card);
        });
        list.append(fragment);
        list.hidden = false;
    };

    byId('quest-state-cycle')?.addEventListener('click', () => {
        if (authenticatedQuestSnapshot === null) return;
        const availableStates = QUEST_STATE_DISPLAY_ORDER.filter((state) =>
            authenticatedQuestSnapshot.quests.some((quest) => quest.state === state));
        if (availableStates.length < 2) return;
        const cycleValues = ['', ...availableStates];
        const currentIndex = cycleValues.indexOf(questStateFilter);
        questStateFilter = cycleValues[(currentIndex + 1) % cycleValues.length];
        setView('quests');
        renderQuestUi();
    });

    const loadMagicItems = async (force = false) => {
        if (magicItemLoading && !force) return magicItemLoading;
        magicItemSnapshot = null;
        magicItemError = '';
        renderMagicItems();
        const refreshButton = byId('magic-items-refresh');
        if (refreshButton instanceof HTMLButtonElement) refreshButton.disabled = true;
        magicItemLoading = (async () => {
            try {
                magicItemSnapshot = {
                    ...(await fetchWikiMagicItems()),
                    data_source: 'wiki'
                };
            } catch {
                try {
                    magicItemSnapshot = {
                        ...(await fetchFallbackMagicItems()),
                        data_source: 'fallback'
                    };
                } catch {
                    magicItemError = 'Magic-item information is unavailable from both the campaign wiki and the bundled fallback.';
                }
            } finally {
                magicItemLoading = null;
                if (refreshButton instanceof HTMLButtonElement) refreshButton.disabled = false;
                renderMagicItems();
            }
        })();
        renderMagicItems();
        return magicItemLoading;
    };

    byId('magic-items-refresh')?.addEventListener('click', () => {
        void loadMagicItems(true);
    });

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
        renderAuthenticatedHeroToken();
        renderXpUi();
        renderWordCountUi();
        renderQuestUi();
        renderMagicItems();
        updatePresencePolling();
    };

    const normalizeHeroName = (value) => String(value || '')
        .normalize('NFKC')
        .trim()
        .toLocaleLowerCase('en-US');

    const loadHeroTokenData = async () => {
        if (heroTokenData !== null) return heroTokenData;
        if (heroTokenDataLoading !== null) return heroTokenDataLoading;
        const validHeroToken = (hero) =>
            typeof hero?.name === 'string'
            && Array.isArray(hero.aliases)
            && hero.aliases.every((alias) => typeof alias === 'string')
            && typeof hero.token === 'string'
            && /^data\/hero-tokens\/[a-z0-9][a-z0-9._-]*\.(?:avif|gif|jpe?g|png|webp)$/iu.test(hero.token)
            && typeof hero.wikiToken === 'string'
            && /^https:\/\/publish-\d+\.obsidian\.md\/access\/[a-z0-9]+\/[^?#]+$/iu.test(hero.wikiToken)
            && (hero.preferLocal === undefined || typeof hero.preferLocal === 'boolean');
        const validPlayerHero = (hero) =>
            validHeroToken(hero)
            && typeof hero.wikiPage === 'string'
            && /^https:\/\/publish\.obsidian\.md\/scarlethorizons\/PCs\/[^?#]+$/iu.test(hero.wikiPage);
        heroTokenDataLoading = fetch('data/heroes.json?v=2', { cache: 'reload' })
            .then(async (response) => {
                if (!response.ok) throw new Error(`HTTP ${response.status}`);
                const payload = await response.json();
                if (payload?.schemaVersion !== 1
                    || !Array.isArray(payload.heroes)
                    || !validHeroToken(payload.dungeonMaster)) {
                    throw new Error('Invalid hero-token data.');
                }
                return {
                    dungeonMaster: payload.dungeonMaster,
                    heroes: payload.heroes.filter(validPlayerHero)
                };
            })
            .catch(() => ({ dungeonMaster: null, heroes: [] }))
            .then((data) => {
                heroTokenData = data;
                return data;
            });
        return heroTokenDataLoading;
    };

    const findAuthenticatedHero = (data, account) => {
        if (!account) return null;
        if (account.role === 'dm') return data.dungeonMaster;
        const accountNames = [
            account.character_name,
            account.character_key,
            String(account.character_name || '').split(/\s+/u)[0]
        ].map(normalizeHeroName).filter(Boolean);
        return data.heroes.find((hero) =>
            [hero.name, ...hero.aliases]
                .map(normalizeHeroName)
                .some((alias) => accountNames.includes(alias))) || null;
    };

    const setHeroTokenImage = (image, hero) => {
        if (!(image instanceof HTMLImageElement)) return;
        image.classList.toggle(
            'is-dungeon-master-token',
            hero?.name === DUNGEON_MASTER_HERO.name);
        if (hero === null) {
            image.hidden = true;
            image.removeAttribute('src');
            image.removeAttribute('data-fallback-src');
            image.classList.remove('is-wiki-link');
            image.tabIndex = -1;
            image.removeAttribute('role');
            image.removeAttribute('title');
            image.removeAttribute('aria-label');
            image.alt = '';
            image.onerror = null;
            image.onclick = null;
            image.onkeydown = null;
            return;
        }
        image.dataset.fallbackSrc = hero.token;
        image.alt = `${hero.name} token`;
        image.onerror = null;
        image.src = hero.token;
        image.hidden = false;
        const wikiPage = typeof hero.wikiPage === 'string' ? hero.wikiPage : '';
        image.classList.toggle('is-wiki-link', wikiPage !== '');
        image.tabIndex = wikiPage === '' ? -1 : 0;
        image.toggleAttribute('role', wikiPage !== '');
        if (wikiPage !== '') {
            image.setAttribute('role', 'link');
            image.title = `click here to go to ${hero.name}'s wiki page...`;
            image.setAttribute('aria-label', image.title);
            const openWikiPage = () => {
                window.open(wikiPage, '_blank', 'noopener,noreferrer');
            };
            image.onclick = openWikiPage;
            image.onkeydown = (event) => {
                if (event.key === 'Enter' || event.key === ' ') {
                    event.preventDefault();
                    openWikiPage();
                }
            };
        } else {
            image.removeAttribute('title');
            image.removeAttribute('aria-label');
            image.onclick = null;
            image.onkeydown = null;
        }

        if (hero.preferLocal === true) return;

        const wikiImage = new Image();
        wikiImage.addEventListener('load', () => {
            if (image.dataset.fallbackSrc === hero.token) {
                image.src = hero.wikiToken;
            }
        }, { once: true });
        wikiImage.src = hero.wikiToken;
    };

    const renderAuthenticatedHeroToken = async () => {
        const accountAtStart = authenticatedAccount;
        if (accountAtStart === null) {
            setHeroTokenImage(byId('auth-dashboard-token'), null);
            setHeroTokenImage(byId('auth-account-token'), null);
            return;
        }
        if (accountAtStart.role === 'dm') {
            setHeroTokenImage(byId('auth-dashboard-token'), DUNGEON_MASTER_HERO);
            setHeroTokenImage(byId('auth-account-token'), DUNGEON_MASTER_HERO);
            return;
        }
        const hero = findAuthenticatedHero(await loadHeroTokenData(), accountAtStart);
        if (authenticatedAccount?.id !== accountAtStart.id) return;
        setHeroTokenImage(byId('auth-dashboard-token'), hero);
        setHeroTokenImage(byId('auth-account-token'), hero);
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

    const validatePresenceSnapshot = (payload) => {
        const validUser = (user) => user
            && typeof user.account_id === 'string'
            && /^[a-f0-9]{32}$/u.test(user.account_id)
            && typeof user.character_name === 'string'
            && user.character_name.length > 0
            && user.character_name.length <= 100
            && ['player', 'dm'].includes(user.role)
            && typeof user.active === 'boolean'
            && (user.last_seen_at === null
                || (typeof user.last_seen_at === 'string'
                    && !Number.isNaN(Date.parse(user.last_seen_at))))
            && (user.last_login_at === null
                || (typeof user.last_login_at === 'string'
                    && !Number.isNaN(Date.parse(user.last_login_at))))
            && (user.active ? user.last_seen_at !== null : user.last_seen_at === null);
        if (!payload
            || payload.schema_version !== 2
            || !['self', 'party'].includes(payload.scope)
            || typeof payload.observed_at !== 'string'
            || Number.isNaN(Date.parse(payload.observed_at))
            || !Number.isSafeInteger(payload.active_window_seconds)
            || payload.active_window_seconds < 30
            || payload.active_window_seconds > 600
            || !Array.isArray(payload.users)
            || payload.users.length > 200
            || !payload.users.every(validUser)
            || (payload.scope === 'self' && payload.users.length !== 0)) {
            throw new Error('The online-user service returned an invalid response.');
        }
        return payload;
    };

    const renderPresenceUi = () => {
        const panel = byId('online-users-summary');
        const status = byId('online-users-status');
        const list = byId('online-users-list');
        const isDungeonMaster = authenticatedAccount?.role === 'dm';
        if (panel) panel.hidden = !isDungeonMaster;
        list?.replaceChildren();
        if (!isDungeonMaster) return;
        if (authenticatedPresenceSnapshot === null) {
            if (status) status.textContent = 'Checking who else is logged in…';
            return;
        }
        const users = authenticatedPresenceSnapshot.users;
        const activeCount = users.filter((user) => user.active).length;
        const inactiveCount = users.length - activeCount;
        if (status) {
            status.textContent = users.length === 0
                ? 'No other user accounts are enabled.'
                : `${activeCount} active now; ${inactiveCount} inactive.`;
        }
        if (list) {
            const fragment = document.createDocumentFragment();
            users.forEach((user) => {
                const item = document.createElement('li');
                item.className = user.active ? 'is-active' : 'is-inactive';
                const name = document.createElement('strong');
                name.textContent = user.character_name;
                const activity = document.createElement('span');
                activity.textContent = user.active
                    ? 'Active now'
                    : user.last_login_at === null
                        ? 'Never logged in'
                        : `Last login ${new Intl.DateTimeFormat(undefined, {
                            dateStyle: 'medium',
                            timeStyle: 'short'
                        }).format(new Date(user.last_login_at))}`;
                item.append(name, activity);
                fragment.append(item);
            });
            list.append(fragment);
        }
    };

    const loadPresence = async () => {
        const requestId = ++presenceRequestId;
        if (authenticatedAccount === null) {
            authenticatedPresenceSnapshot = null;
            renderPresenceUi();
            return;
        }
        const accountId = authenticatedAccount.id;
        try {
            const snapshot = validatePresenceSnapshot(
                await requestAuthenticationApi('/presence'));
            if (requestId !== presenceRequestId || authenticatedAccount?.id !== accountId) return;
            authenticatedPresenceSnapshot = snapshot;
            renderPresenceUi();
        } catch (error) {
            if (requestId !== presenceRequestId || authenticatedAccount?.id !== accountId) return;
            authenticatedPresenceSnapshot = null;
            renderPresenceUi();
            const status = byId('online-users-status');
            if (status && authenticatedAccount?.role === 'dm') status.textContent = error.message;
        }
    };

    const updatePresencePolling = () => {
        if (presencePollTimer !== 0) {
            window.clearInterval(presencePollTimer);
            presencePollTimer = 0;
        }
        presenceRequestId++;
        authenticatedPresenceSnapshot = null;
        renderPresenceUi();
        if (authenticatedAccount === null) return;
        void loadPresence();
        presencePollTimer = window.setInterval(() => {
            void loadPresence();
        }, 30000);
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
        authenticatedXpSnapshot = null;
        authenticatedWordCountSnapshot = null;
        authenticatedPresenceSnapshot = null;
        authenticatedQuestSnapshot = null;
        questStateFilter = '';
        lastQuestAlertSignature = '';
        updateAuthenticationUi();
        if (authenticatedAccount !== null) {
            await Promise.all([loadXpSummary(), loadWordCountSummary(), loadQuests()]);
        }
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
    byId('quest-alert-close')?.addEventListener('click', () => {
        const questAlertDialog = byId('quest-alert-dialog');
        if (questAlertDialog instanceof HTMLDialogElement) questAlertDialog.close();
    });
    authDialog?.addEventListener('close', () => {
        void renderAuthenticatedHeroToken();
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
            authenticatedXpSnapshot = null;
            authenticatedWordCountSnapshot = null;
            authenticatedQuestSnapshot = null;
            questStateFilter = '';
            lastQuestAlertSignature = '';
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
            await Promise.all([loadXpSummary(), loadWordCountSummary(), loadQuests()]);
        } catch (error) {
            authenticatedAccount = null;
            authenticationCsrfToken = '';
            authenticatedXpSnapshot = null;
            authenticatedWordCountSnapshot = null;
            authenticatedQuestSnapshot = null;
            questStateFilter = '';
            lastQuestAlertSignature = '';
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
            authenticatedXpSnapshot = null;
            xpRequestId++;
            authenticatedWordCountSnapshot = null;
            wordCountRequestId++;
            authenticatedQuestSnapshot = null;
            questRequestId++;
            questStateFilter = '';
            lastQuestAlertSignature = '';
            const questAlertDialog = byId('quest-alert-dialog');
            if (questAlertDialog instanceof HTMLDialogElement && questAlertDialog.open) {
                questAlertDialog.close();
            }
            updateAuthenticationUi();
            if (authDialog instanceof HTMLDialogElement) authDialog.close();
        } catch (error) {
            setAuthenticationMessage(error.message, true, true);
        } finally {
            logoutButton.disabled = false;
        }
    });

    byId('xp-refresh')?.addEventListener('click', () => {
        void loadXpSummary();
    });

    byId('word-count-refresh')?.addEventListener('click', () => {
        void loadWordCountSummary();
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
        const replacingExistingWorker = navigator.serviceWorker.controller !== null;
        let reloadingForServiceWorker = false;
        navigator.serviceWorker.addEventListener('controllerchange', () => {
            if (replacingExistingWorker && !reloadingForServiceWorker) {
                reloadingForServiceWorker = true;
                window.location.reload();
            }
        });
        window.addEventListener('load', async () => {
            try {
                const registration = await navigator.serviceWorker.register(
                    'service-worker.js',
                    { scope: './', updateViaCache: 'none' });
                await registration.update();
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
