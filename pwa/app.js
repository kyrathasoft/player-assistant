import { initializeTranslator } from './modules/translator.js?v=100';
import { initializeCampaignSearch } from './modules/search.js?v=100';
import { initializeDice } from './modules/dice.js?v=100';
import { createControllerChangeHandler } from './service-worker-controller.js?v=100';
import { mergeInboxSnapshot, createMessageDraftStore } from './modules/inbox-state.js?v=100';
import { createAccountSessionController } from './modules/account-session.js?v=100';
import { createMessagesActivityController } from './modules/messages-activity.js?v=100';
import { assertXpRecords, assertAwardRecords, assertScopedMessages } from './data-invariants.js?v=100';
import { createPresenceController } from './modules/presence.js?v=100';
import { createUpdateLifecycleController } from './modules/update-lifecycle.js?v=100';
import { createCorrelationContext, correlationHeaders } from './correlation.js?v=100';
import { createOfflineActionQueue, MUTATING_METHODS, QUEUE_STATES } from './modules/offline-action-queue.js?v=100';

(() => {
    'use strict';

    const APP_NAME = 'Player Assistant';
    const CORRELATION_CONTEXT = createCorrelationContext();
    const CORRELATION_HEADERS = correlationHeaders(CORRELATION_CONTEXT);
    const APP_VERSION = globalThis.PLAYER_ASSISTANT_VERSION_METADATA?.pwaVersion;
    if (!APP_VERSION) {
        throw new Error('Player Assistant version metadata is unavailable.');
    }
    const AUTH_API_ROOT = '/scarlethorizons/api/v1';
    const MAGIC_ITEM_LONGEVITY_VALUES = Object.freeze(['one-shot', 'limited-use', 'permanent']);
    const PARTY_FUNDS_GEMSTONE_VALUE_PATTERN = /^\s*(\d+(?:\.\d+)?)\s+gp$/i;
    const MAXIMUM_XP_AWARD_PROGRESSION_ENTRIES = 1000;
    const DUNGEON_MASTER_HERO = Object.freeze({
        name: 'Dungeon Master',
        aliases: ['Dungeon Master'],
        token: 'data/hero-tokens/dungeon-master.webp',
        preferLocal: true
    });
    const QUEST_STATE_VALUES = Object.freeze([
        'gated',
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
        'gated',
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
        gated: 'Gated',
        completed: 'Completed',
        withdrawn: 'Withdrawn'
    });
    const canViewXpAwards = (account) => account !== null;

    const byId = (id) => document.getElementById(id);
    const views = new Map(
        [...document.querySelectorAll('[data-view-panel]')]
            .map((element) => [element.dataset.viewPanel, element]));
    const navButtons = [...document.querySelectorAll('[data-view]')];
    const protectedNavViews = new Set(['quests', 'activity', 'magic-items', 'party-funds', 'xp-awards']);

    let deferredInstallPrompt = null;
    let authenticatedAccount = null;
    let authenticationCsrfToken = '';
    let authenticatedXpSnapshot = null;
    let xpRequestId = 0;
    let authenticatedXpAwardsSnapshot = null;
    let xpAwardsLoading = null;
    let xpAwardsRequestId = 0;
    let xpAwardsError = '';
    let pendingLevelUpNotifications = [];
    let pendingLevelUpAcknowledgements = [];
    let levelUpAcknowledgementTimer = 0;
    let authenticatedWordCountSnapshot = null;
    let wordCountRequestId = 0;
    let authenticatedPresenceSnapshot = null;
    let presenceRequestId = 0;

    let authenticatedQuestSnapshot = null;
    let questRequestId = 0;
    let questStateFilter = '';
    let lastQuestAlertSignature = '';
    let authenticatedMessageSnapshot = null;
    let messageRequestId = 0;
    let messageLoading = false;
    let messageError = '';
    let messageDraftStore = null;
    let authenticatedRevisionSnapshot = null;
    let appliedMessageRevision = null;
    let appliedQuestRevision = null;
    let revisionRequestId = 0;
    let revisionPollTimer = 0;
    let revisionsUpdatedAt = 0;
    let authenticationGeneration = 0;
    const seenProtectedResponseNonces = new Set();
    let authenticatedResourceGeneration = '';
    let authenticatedAbsoluteExpiresAt = 0;
    const activeAuthenticationControllers = new Set();
    const AUTH_REQUEST_TIMEOUT_MS = 15000;
    let magicItemSnapshot = null;
    let magicItemLoading = null;
    let magicItemError = '';
    let magicItemRequestId = 0;
    let partyFundsSnapshot = null;
    let partyFundsLoading = null;
    let partyFundsError = '';
    let heroTokenData = null;
    let heroTokenDataLoading = null;
    let activeView = 'dashboard';
    let xpUpdatedAt = 0;
    let questsUpdatedAt = 0;
    let xpAwardsUpdatedAt = 0;
    let magicItemsUpdatedAt = 0;
    let partyFundsUpdatedAt = 0;
    let messagesUpdatedAt = 0;

    const renderFreshness = (id, timestamp) => {
        const element = byId(id);
        if (!(element instanceof HTMLElement)) return;
        element.hidden = timestamp <= 0;
        element.textContent = timestamp > 0
            ? `Last refreshed ${new Date(timestamp).toLocaleString()}.`
            : '';
    };

    const clearProtectedFreshness = () => {
        xpUpdatedAt = 0;
        questsUpdatedAt = 0;
        xpAwardsUpdatedAt = 0;
        magicItemsUpdatedAt = 0;
        partyFundsUpdatedAt = 0;
        messagesUpdatedAt = 0;
        revisionsUpdatedAt = 0;
    };

    const resetMagicItemState = () => {
        magicItemRequestId++;
        magicItemSnapshot = null;
        magicItemLoading = null;
        magicItemError = '';
        magicItemsUpdatedAt = 0;
    };

    const setView = (viewName, updateHistory = true) => {
        const requestedView = views.has(viewName) ? viewName : 'dashboard';
        const isAuthenticated = authenticatedAccount !== null;
        const isDungeonMaster = isAuthenticated && authenticatedAccount.role === 'dm';
        const canMessagePlayer = isAuthenticated
            && (isDungeonMaster
                || (authenticatedMessageSnapshot?.player_recipients.length || 0) > 0);
        const resolvedView = !isAuthenticated
            ? (requestedView === 'message-dm' || requestedView === 'message-player')
                ? 'dashboard'
                : protectedNavViews.has(requestedView)
                    ? 'dashboard'
                    : requestedView
            : (requestedView === 'message-dm' && isDungeonMaster)
                ? 'dashboard'
                : (requestedView === 'message-player' && !canMessagePlayer)
                    ? 'dashboard'
                    : (requestedView === 'xp-awards' && !canViewXpAwards(authenticatedAccount))
                        ? 'dashboard'
                    : requestedView;
        activeView = resolvedView;
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
            void campaignSearch.load();
        }
        if (resolvedView === 'quests'
            && authenticatedAccount !== null
            && authenticatedQuestSnapshot === null) {
            void loadQuests();
        }
        if (resolvedView === 'magic-items'
            && authenticatedAccount !== null
            && magicItemSnapshot === null) {
            void loadMagicItems();
        }
        if (resolvedView === 'party-funds'
            && authenticatedAccount !== null
            && partyFundsSnapshot === null) {
            void loadPartyFunds();
        }
        if (resolvedView === 'xp-awards'
            && authenticatedAccount !== null
            && authenticatedXpAwardsSnapshot === null
            && xpAwardsError === '') {
            void loadXpAwards();
        }
        if (resolvedView === 'message-dm') {
            renderMessageDmUi();
        }
        if (resolvedView === 'message-player') {
            renderMessagePlayerUi();
        }

        byId('main-content')?.focus({ preventScroll: true });
        window.scrollTo({
            top: 0,
            behavior: window.matchMedia('(prefers-reduced-motion: reduce)').matches
                ? 'auto'
                : 'smooth'
        });
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
    window.addEventListener('hashchange', () => setView(location.hash.slice(1), false));
    window.addEventListener('load', () => {
        const requestedView = location.hash.slice(1) || 'dashboard';
        if (!protectedNavViews.has(requestedView)) setView(requestedView, false);
    });

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
        const retryButton = byId('xp-retry');
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
        if (retryButton) retryButton.hidden = !authenticated || authenticatedXpSnapshot !== null;
        renderFreshness('xp-freshness', authenticated ? xpUpdatedAt : 0);
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
        const validXpAwardDate = (value) => typeof value === 'string'
            && value.length > 0
            && value.length <= 200
            && !/[\x00-\x1F\x7F]/u.test(value);
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
            && Number.isSafeInteger(character.level_before_award)
            && character.level_before_award >= 0
            && character.level_before_award <= 1000
            && Number.isSafeInteger(character.xp_award)
            && character.xp_award >= 0
            && validXpAwardDate(character.xp_award_date)
            && Number.isSafeInteger(character.level_after_award)
            && character.level_after_award >= 0
            && character.level_after_award <= 1000
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
                    && character.xp_to_next_level >= 0))
            && ((character.level_up_target_level === null
                && character.level_up_target_xp === null
                && character.level_up_attained === null)
                || (Number.isSafeInteger(character.level_up_target_level)
                    && character.level_up_target_level >= 1
                    && character.level_up_target_level <= 1000
                    && Number.isSafeInteger(character.level_up_target_xp)
                    && character.level_up_target_xp >= 0
                    && typeof character.level_up_attained === 'boolean'));
        if (payload.scope === 'character'
            && validCharacter(payload.character)
            && Array.isArray(payload.authorized_characters)
            && payload.authorized_characters.length > 0
            && payload.authorized_characters.length <= 200
            && payload.authorized_characters.every((entry) => entry
                && typeof entry.character_key === 'string'
                && /^[a-z0-9]+(?:-[a-z0-9]+)*$/u.test(entry.character_key)
                && validCharacter(entry.character))) {
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
            xpUpdatedAt = Date.now();
            renderXpUi();
            renderXpAwardsUi();
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

    const formatXpProgressAmount = (character) => {
        if (!character || character.xp_to_next_level === null) return '';
        const nextLevelXp = character.xp_total + character.xp_to_next_level;
        if (!Number.isSafeInteger(nextLevelXp) || nextLevelXp <= 0) return '';
        const percentage = new Intl.NumberFormat('en-US', {
            minimumFractionDigits: 1,
            maximumFractionDigits: 1,
            useGrouping: false
        }).format((character.xp_total / nextLevelXp) * 100);
        return `${percentage}% of the way toward ${character.character_class} Level ${character.level + 1}`;
    };

    const formatXpProgressSummary = (character) => {
        const progressAmount = formatXpProgressAmount(character);
        return progressAmount === '' ? '' : `${character.character_name} is ${progressAmount}`;
    };

    const resetLevelUpNotificationState = () => {
        pendingLevelUpNotifications = [];
        pendingLevelUpAcknowledgements = [];
        if (levelUpAcknowledgementTimer !== 0) {
            clearTimeout(levelUpAcknowledgementTimer);
            levelUpAcknowledgementTimer = 0;
        }
    };

    const acknowledgeDisplayedLevelUpNotifications = async () => {
        if (pendingLevelUpAcknowledgements.length === 0 || authenticatedAccount === null) return;
        const batch = pendingLevelUpAcknowledgements.map((notification) => ({
            character_key: notification.character_key,
            target_level: notification.target_level
        }));
        try {
            const payload = await requestAuthenticationApi('/xp-level-up-notifications/acknowledge', {
                method: 'POST',
                body: { notifications: batch },
                csrf: true
            });
            if (payload.schema_version !== 1
                || !Number.isSafeInteger(payload.acknowledged_count)
                || payload.acknowledged_count < 0
                || payload.acknowledged_count > batch.length) {
                throw new Error('The level-up acknowledgement response was invalid.');
            }
            pendingLevelUpAcknowledgements = [];
        } catch {
            if (authenticatedAccount !== null && pendingLevelUpAcknowledgements.length > 0) {
                levelUpAcknowledgementTimer = window.setTimeout(() => {
                    levelUpAcknowledgementTimer = 0;
                    void acknowledgeDisplayedLevelUpNotifications();
                }, 5000);
            }
        }
    };

    const showPendingLevelUpNotifications = () => {
        const dialog = byId('level-up-alert-dialog');
        const list = byId('level-up-alert-list');
        const auth = byId('auth-dialog');
        if (!(dialog instanceof HTMLDialogElement)
            || !(list instanceof HTMLElement)
            || pendingLevelUpNotifications.length === 0
            || dialog.open
            || (auth instanceof HTMLDialogElement && auth.open)) return;
        const displayedNotifications = pendingLevelUpNotifications;
        list.replaceChildren(...displayedNotifications.map((notification) => {
            const item = document.createElement('li');
            item.textContent = `${notification.character_name} reached ${notification.character_class} Level ${notification.target_level}`;
            return item;
        }));
        dialog.showModal();
        pendingLevelUpNotifications = [];
        pendingLevelUpAcknowledgements = displayedNotifications;
        void acknowledgeDisplayedLevelUpNotifications();
    };

    const claimLevelUpNotifications = async () => {
        const payload = await requestAuthenticationApi('/xp-level-up-notifications/claim', {
            method: 'POST',
            csrf: true
        });
        if (payload.schema_version !== 1
            || !Array.isArray(payload.notifications)
            || payload.notifications.length > 200
            || !payload.notifications.every((notification) => notification
                && typeof notification.character_key === 'string'
                && /^[a-z0-9][a-z0-9._:-]{0,99}$/u.test(notification.character_key)
                && typeof notification.character_name === 'string'
                && notification.character_name.length >= 1
                && notification.character_name.length <= 200
                && typeof notification.character_class === 'string'
                && notification.character_class.length >= 1
                && notification.character_class.length <= 200
                && Number.isSafeInteger(notification.target_level)
                && notification.target_level >= 1
                && notification.target_level <= 1000)) {
            throw new Error('The level-up notification response was invalid.');
        }
        pendingLevelUpNotifications = payload.notifications;
        showPendingLevelUpNotifications();
    };

    const renderXpAwardsUi = () => {
        const status = byId('xp-awards-status');
        const list = byId('xp-awards-list');
        const retryButton = byId('xp-awards-retry');
        if (!(status instanceof HTMLElement) || !(list instanceof HTMLElement)) return;
        if (retryButton) retryButton.hidden = authenticatedAccount === null
            || (xpAwardsError === '' && xpAwardsLoading !== null)
            || authenticatedXpAwardsSnapshot !== null;
        renderFreshness('xp-awards-freshness', authenticatedAccount === null ? 0 : xpAwardsUpdatedAt);
        list.hidden = true;
        list.replaceChildren();
        if (authenticatedAccount === null) {
            status.textContent = 'Log in to view XP progressions.';
            return;
        }
        if (xpAwardsLoading !== null) {
            status.textContent = 'Loading XP progressions…';
            return;
        }
        if (xpAwardsError !== '') {
            status.textContent = xpAwardsError;
            return;
        }
        if (authenticatedXpAwardsSnapshot === null) {
            status.textContent = 'XP progressions load when this view is opened.';
            return;
        }
        status.textContent = '';
        const fragment = document.createDocumentFragment();
        authenticatedXpAwardsSnapshot.forEach(({ characterKey, currentCharacter, entries }) => {
            const character = entries[0];
            const progressionCharacterKey = characterKey.endsWith('-xp')
                ? characterKey.slice(0, -3)
                : characterKey;
            const currentProgression = authenticatedXpSnapshot?.scope === 'character'
                ? authenticatedXpSnapshot.authorized_characters.find(
                    (entry) => entry.character_key === characterKey
                        || entry.character_key === progressionCharacterKey)?.character
                : authenticatedXpSnapshot?.scope === 'party'
                    ? (Array.isArray(authenticatedXpSnapshot.characters)
                        ? (authenticatedXpSnapshot.characters.find(
                            (entry) => entry.character_key === characterKey
                                || entry.character_key === progressionCharacterKey))
                        : null)
                    : null;
            const displayProgression = currentCharacter || currentProgression;
            const headingName = authenticatedAccount?.role === 'dm'
                ? character.character_name
                : displayProgression?.character_name || character.character_name;
            const card = document.createElement('article');
            card.className = 'xp-award-character';
            const heading = document.createElement('div');
            heading.className = 'xp-award-character-heading';
            const name = document.createElement('h2');
            name.textContent = headingName;
            const characterClass = document.createElement('span');
            characterClass.textContent = character.character_class;
            if (displayProgression) {
                if (authenticatedAccount?.role === 'dm') {
                    const xpTotal = Number.isSafeInteger(displayProgression.xp_total)
                        ? Number(displayProgression.xp_total).toLocaleString('en-US')
                        : '';
                    const tnl = displayProgression.xp_to_next_level === null
                        ? ''
                        : Number(displayProgression.xp_to_next_level).toLocaleString('en-US');
                    if (xpTotal !== '' || tnl !== '') {
                        const currentXp = document.createElement('span');
                        currentXp.className = 'xp-award-current-total';
                        currentXp.textContent = xpTotal === ''
                            ? ` - TNL: ${tnl}`
                            : tnl === ''
                                ? ` - ${xpTotal} XP`
                                : ` - ${xpTotal} XP (TNL: ${tnl})`;
                        name.append(currentXp);
                    }
                } else {
                    const progressAmount = formatXpProgressAmount(displayProgression);
                    if (progressAmount !== '') {
                        const progress = document.createElement('span');
                        progress.className = 'xp-award-progress-summary';
                        progress.textContent = ` - Progress: ${progressAmount}`;
                        name.append(progress);
                    }
                }
            }
            heading.append(name, characterClass);
            const table = document.createElement('table');
            const thead = document.createElement('thead');
            const headerRow = document.createElement('tr');
            ['Date', 'XP award', 'Level'].forEach((label) => {
                const cell = document.createElement('th');
                cell.scope = 'col';
                cell.textContent = label;
                headerRow.append(cell);
            });
            thead.append(headerRow);
            const tbody = document.createElement('tbody');
            entries.forEach((entry) => {
                const row = document.createElement('tr');
                const date = document.createElement('td');
                const award = document.createElement('td');
                const level = document.createElement('td');
                date.textContent = entry.xp_award_date;
                award.textContent = Number(entry.xp_award).toLocaleString('en-US');
                level.textContent = `${entry.level_before_award} → ${entry.level_after_award}`;
                row.append(date, award, level);
                tbody.append(row);
            });
            table.append(thead, tbody);
            card.append(heading, table);
            fragment.append(card);
        });
        const progressCharacters = authenticatedXpSnapshot?.scope === 'party'
            ? authenticatedXpSnapshot.characters
            : [];
        const progressItems = progressCharacters
            .map((character) => formatXpProgressSummary(character))
            .filter((summary) => summary !== '');
        if (progressItems.length > 0) {
            const progressSection = document.createElement('section');
            progressSection.className = 'xp-award-progress-section';
            const progressHeading = document.createElement('h2');
            progressHeading.textContent = 'Progress toward next class level';
            const progressList = document.createElement('ul');
            progressList.className = 'xp-award-progress-list';
            progressItems.forEach((summary) => {
                const item = document.createElement('li');
                item.textContent = summary;
                progressList.append(item);
            });
            progressSection.append(progressHeading, progressList);
            fragment.append(progressSection);
        }
        list.append(fragment);
        list.hidden = false;
    };

    const validateXpAwardsEntries = (payload) => {
        if (!Array.isArray(payload)
            || payload.length === 0
            || payload.length > MAXIMUM_XP_AWARD_PROGRESSION_ENTRIES) {
            throw new Error('An XP progression file was invalid.');
        }
        const validEntry = (entry) => entry
            && typeof entry.character_name === 'string'
            && entry.character_name.length > 0
            && entry.character_name.length <= 100
            && typeof entry.character_class === 'string'
            && entry.character_class.length > 0
            && entry.character_class.length <= 100
            && Number.isSafeInteger(entry.level_before_award)
            && entry.level_before_award >= 0
            && Number.isSafeInteger(entry.xp_award)
            && entry.xp_award >= 0
            && typeof entry.xp_award_date === 'string'
            && entry.xp_award_date.length > 0
            && entry.xp_award_date.length <= 200
            && Number.isSafeInteger(entry.level_after_award)
            && entry.level_after_award >= 0;
        if (!payload.every(validEntry)) throw new Error('An XP progression file was invalid.');
        assertAwardRecords(payload);
        const characterName = payload[0].character_name;
        if (!payload.every((entry) => entry.character_name === characterName)) {
            throw new Error('An XP progression file contained multiple characters.');
        }
        return payload;
    };

    const validateXpAwardsSnapshot = (payload) => {
        if (!payload
            || payload.schema_version !== 1
            || !['character', 'party'].includes(payload.scope)
            || !Array.isArray(payload.progressions)
            || payload.progressions.length === 0
            || payload.progressions.length > 200) {
            throw new Error('The XP Awards response was invalid.');
        }
        return payload.progressions.map((progression) => {
            if (!progression
                || typeof progression.character_key !== 'string'
                || !/^[a-z0-9]+(?:-[a-z0-9]+)*$/u.test(progression.character_key)
                || (payload.scope === 'character' && typeof progression.is_account_character !== 'boolean')) {
                throw new Error('The XP Awards response was invalid.');
            }
            const currentCharacter = progression.current_character;
            if (currentCharacter !== undefined
                && (!currentCharacter
                    || typeof currentCharacter.character_name !== 'string'
                    || currentCharacter.character_name.length === 0
                    || currentCharacter.character_name.length > 100
                    || typeof currentCharacter.character_class !== 'string'
                    || currentCharacter.character_class.length === 0
                    || currentCharacter.character_class.length > 100
                    || !Number.isSafeInteger(currentCharacter.level)
                    || currentCharacter.level < 0
                    || !Number.isSafeInteger(currentCharacter.xp_total)
                    || currentCharacter.xp_total < 0
                    || (currentCharacter.xp_to_next_level !== null
                        && (!Number.isSafeInteger(currentCharacter.xp_to_next_level)
                            || currentCharacter.xp_to_next_level < 0)))) {
                throw new Error('The XP Awards response was invalid.');
            }
            if (currentCharacter !== undefined
                && !((currentCharacter.level_up_target_level === null
                    && currentCharacter.level_up_target_xp === null
                    && currentCharacter.level_up_attained === null)
                    || (Number.isSafeInteger(currentCharacter.level_up_target_level)
                        && currentCharacter.level_up_target_level >= 1
                        && currentCharacter.level_up_target_level <= 1000
                        && Number.isSafeInteger(currentCharacter.level_up_target_xp)
                        && currentCharacter.level_up_target_xp >= 0
                        && typeof currentCharacter.level_up_attained === 'boolean'))) {
                throw new Error('The XP Awards response was invalid.');
            }
            return {
                characterKey: progression.character_key,
                isAccountCharacter: progression.is_account_character === true,
                currentCharacter: currentCharacter || null,
                entries: validateXpAwardsEntries(progression.entries)
            };
        }).filter((progression, index, progressions) => {
            if (payload.scope !== 'character') return true;
            const primaryCount = progressions.filter((entry) => entry.isAccountCharacter).length;
            if (primaryCount !== 1) throw new Error('The XP Awards response was invalid.');
            return true;
        });
    };

    const loadXpAwards = async (force = false) => {
        if (xpAwardsLoading !== null && !force) return xpAwardsLoading;
        const requestId = ++xpAwardsRequestId;
        authenticatedXpAwardsSnapshot = null;
        xpAwardsError = '';
        renderXpAwardsUi();
        const account = authenticatedAccount;
        if (account === null) return;
        xpAwardsLoading = (async () => {
            try {
                const progressions = validateXpAwardsSnapshot(
                    await requestAuthenticationApi('/xp-awards'));
                if (requestId !== xpAwardsRequestId || authenticatedAccount?.id !== account.id) return;
                authenticatedXpAwardsSnapshot = progressions;
                xpAwardsUpdatedAt = Date.now();
            } catch (error) {
                if (requestId === xpAwardsRequestId && authenticatedAccount?.id === account.id) {
                    xpAwardsError = error instanceof Error ? error.message : 'XP progressions are unavailable.';
                }
            } finally {
                if (requestId === xpAwardsRequestId) {
                    xpAwardsLoading = null;
                    renderXpAwardsUi();
                }
            }
        })();
        renderXpAwardsUi();
        return xpAwardsLoading;
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
            && ['gated', 'available', 'active', 'available (abandoned)', 'completed', 'withdrawn'].includes(quest.state)
            && Array.isArray(quest.objectives)
            && quest.objectives.length > 0
            && quest.objectives.length <= 20
            && quest.objectives.every((objective) => validRequiredText(objective, 500))
            && validShortText(quest.reward, 500)
            && validShortText(quest.accepted_on, 100)
            && validShortText(quest.expires_on, 100)
            && validShortText(quest.completed_on, 100)
            && validShortText(quest.completed_meta_date, 100)
            && (quest.request_status === null
                || QUEST_REQUEST_STATUS_VALUES.includes(quest.request_status))
            && /^https:\/\/publish\.obsidian\.md\/scarlethorizons\/(?:Locations|Meta|NPCs|Player-Contributed|Powers|Quests|Writings)\/[^?#]+$/u.test(quest.wiki_url);
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

    const updateQuestNavCount = (count = 0) => {
        const label = Number.isFinite(count) && count >= 0
            ? `Quests (${count})`
            : 'Quests';
        navButtons
            .filter((button) => button.dataset.view === 'quests')
            .forEach((button) => {
                button.textContent = label;
            });
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
            authenticatedRevisionSnapshot = null;
            await Promise.all([loadQuests(), loadRevisions()]);
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
            authenticatedRevisionSnapshot = null;
            await Promise.all([loadQuests(), loadRevisions()]);
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
            authenticatedRevisionSnapshot = null;
            await Promise.all([loadQuests(), loadRevisions()]);
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
        const retryButton = byId('quests-retry');
        const stateCycle = byId('quest-state-cycle');
        const stateCycleLabel = byId('quest-state-cycle-label');
        list?.replaceChildren();
        if (list) list.hidden = true;
        if (stateCycle) stateCycle.hidden = true;
        updateQuestNavCount(0);
        if (retryButton) retryButton.hidden = authenticatedAccount === null
            || authenticatedQuestSnapshot !== null;
        renderFreshness('quests-freshness', authenticatedAccount === null ? 0 : questsUpdatedAt);

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
        updateQuestNavCount(visibleQuests.length);

        if (status) {
            status.textContent = questStateFilter === ''
                ? ''
                : `Showing ${visibleQuests.length} ${QUEST_STATUS_LABELS[questStateFilter]} quest${visibleQuests.length === 1 ? '' : 's'}.`;
        }
        if (!list) return;
        const fragment = document.createDocumentFragment();
        visibleQuests.forEach((quest, questIndex) => {
            const card = document.createElement('article');
            card.className = 'quest-card';

            const heading = document.createElement('header');
            heading.className = 'quest-card-heading';
            const title = document.createElement('h2');
            title.textContent = `${questIndex + 1}. ${quest.title}`;
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
            if (quest.state === 'completed'
                && quest.completed_on.trim() !== ''
                && quest.completed_meta_date.trim() !== '') {
                appendQuestDetail(
                    details,
                    'Achieved',
                    `${quest.completed_on} (${quest.completed_meta_date})`);
            }
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
            return false;
        }
        const accountId = authenticatedAccount.id;
        authenticatedQuestSnapshot = null;
        renderQuestUi();
        try {
            const snapshot = validateQuestSnapshot(
                await requestAuthenticationApi('/quests'));
            if (requestId !== questRequestId || authenticatedAccount?.id !== accountId) return false;
            authenticatedQuestSnapshot = snapshot;
            questsUpdatedAt = Date.now();
            renderQuestUi();
            renderActivityUi();
            return true;
        } catch (error) {
            if (requestId !== questRequestId || authenticatedAccount?.id !== accountId) return false;
            const status = byId('quests-status');
            if (status) status.textContent = error.message;
            return false;
        }
    };

    const validMagicItemText = (value, maximum = 4000) =>
        typeof value === 'string' && value.trim().length > 0 && value.length <= maximum;

    const getMagicItemViewers = (value) => String(value || 'all')
        .split(',')
        .map((viewer) => viewer.trim().toLowerCase())
        .filter((viewer) => viewer !== '');

    const isMagicItemVisible = (item) => getMagicItemViewers(item?.['viewable-by']).includes('all');

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
            || !['broker', 'fallback'].includes(payload.source)
            || !Array.isArray(payload.items)
            || payload.items.length > 100
            || !payload.items.every(validItem)) {
            throw new Error('Magic-item data is invalid.');
        }
        return payload;
    };

    const fetchBrokerMagicItems = async () => validateMagicItems(
        await requestAuthenticationApi('/magic-items'));

    const fetchFallbackMagicItems = async () => {
        const response = await fetch('magic-items.json', { cache: 'no-cache' });
        if (!response.ok) throw new Error('The bundled public magic-item fallback is unavailable.');
        const payload = await response.json();
        const normalized = validateMagicItems({ ...payload, source: 'fallback' });
        if (normalized.items.some((item) => !isMagicItemVisible(item))) {
            throw new Error('The bundled magic-item fallback contains protected records.');
        }
        return normalized;
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
        renderFreshness('magic-items-freshness', authenticatedAccount === null ? 0 : magicItemsUpdatedAt);
        list?.replaceChildren();
        if (list) list.hidden = true;
        if (counts) counts.hidden = true;
        if (magicItemSnapshot === null) {
            if (status) {
                status.textContent = magicItemLoading
                    ? 'Loading magic items…'
                    : (magicItemError || (authenticatedAccount === null
                        ? 'Log in as your character to view magic items.'
                        : 'Magic items load when this page is opened.'));
            }
            return;
        }
        if (status) {
            status.textContent = magicItemSnapshot.data_source === 'broker'
                ? 'Authorized magic items loaded from the private campaign service.'
                : 'The private campaign service is unavailable; showing the public fallback.';
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

    const validPartyFundsText = (value, minimum = 1, maximum = 5000) => {
        const text = String(value || '').trim();
        return text.length >= minimum && text.length <= maximum;
    };

    const parsePartyFundsGemstoneValue = (value) => {
        const match = PARTY_FUNDS_GEMSTONE_VALUE_PATTERN.exec(String(value || ''));
        return match ? Number(match[1]) : NaN;
    };

    const validatePartyFunds = (payload) => {
        const validCoins = (coins) => coins
            && Number.isSafeInteger(coins.copper)
            && coins.copper >= 0
            && Number.isSafeInteger(coins.silver)
            && coins.silver >= 0
            && Number.isSafeInteger(coins.gold)
            && coins.gold >= 0;
        const validGemstone = (gemstone) => gemstone
            && validPartyFundsText(gemstone.type, 2, 40)
            && validPartyFundsText(gemstone.size, 2, 40)
            && validPartyFundsText(gemstone.quality, 2, 40)
            && PARTY_FUNDS_GEMSTONE_VALUE_PATTERN.test(String(gemstone.value || ''))
            && Number.isFinite(parsePartyFundsGemstoneValue(gemstone.value));
        if (!payload
            || payload.schema_version !== 2
            || !validPartyFundsText(payload['meta-date'], 1, 40)
            || !validPartyFundsText(payload['fiction-date'], 1, 60)
            || !validPartyFundsText(payload.text, 1, 5000)
            || !validCoins(payload.coins)
            || !Array.isArray(payload.gemstones)
            || payload.gemstones.length > 100
            || !payload.gemstones.every(validGemstone)) {
            throw new Error('Party-funds data is invalid.');
        }
        return payload;
    };

    const getPartyFundsGpValue = (coins) => Number(coins.gold)
        + (Number(coins.silver) / 10)
        + (Number(coins.copper) / 100);

    const getPartyFundsTotal = (funds) => getPartyFundsGpValue(funds.coins)
        + funds.gemstones
            .map((gemstone) => parsePartyFundsGemstoneValue(gemstone.value))
            .reduce((sum, value) => sum + value, 0);

    const formatPartyFundsTotal = (value) => {
        const rounded = Math.round(value * 100) / 100;
        return Number.isInteger(rounded)
            ? `${rounded.toLocaleString('en-US')} gp`
            : `${rounded.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} gp`;
    };

    const fetchPartyFunds = async () => {
        const response = await fetch('data/party-funds.json', { cache: 'no-cache' });
        if (!response.ok) {
            throw new Error('The bundled party-funds file is unavailable.');
        }
        return validatePartyFunds(await response.json());
    };

    const parsePartyFundsEntries = (value) => String(value || '')
        .split(/\n\s*---\s*\n/u)
        .map((entry, index) => {
            const lines = entry.trim().split(/\r?\n/u);
            const date = String(lines.shift() || '').trim();
            const match = /^(\d{1,2})\/(\d{1,2})\/(\d{4})$/u.exec(date);
            const timestamp = match
                ? Date.UTC(Number(match[3]), Number(match[1]) - 1, Number(match[2]))
                : Number.NEGATIVE_INFINITY;
            return {
                date,
                text: lines.join('\n').trim(),
                timestamp,
                index
            };
        })
        .filter((entry) => entry.date.length > 0 && entry.text.length > 0)
        .sort((left, right) => right.timestamp - left.timestamp || left.index - right.index);

    const renderPartyFunds = () => {
        const status = byId('party-funds-status');
        const total = byId('party-funds-total');
        const note = byId('party-funds-note');
        renderFreshness('party-funds-freshness', authenticatedAccount === null ? 0 : partyFundsUpdatedAt);
        if (status) {
            status.textContent = partyFundsSnapshot === null
                ? (partyFundsLoading
                    ? 'Loading party funds\u2026'
                    : (partyFundsError || (authenticatedAccount === null
                        ? 'Log in as your character to view party funds.'
                        : 'Party funds load when this view is opened.')))
                : 'Current party funds loaded from the bundled file.';
        }
        if (total) {
            total.textContent = partyFundsSnapshot === null
                ? (authenticatedAccount === null ? '' : '\u2014')
                : formatPartyFundsTotal(getPartyFundsTotal(partyFundsSnapshot));
        }
        if (!note) return;
        if (partyFundsSnapshot === null) {
            note.hidden = true;
            note.replaceChildren();
            return;
        }
        const noteText = String(partyFundsSnapshot?.text || '').trim();
        if (noteText.length === 0) {
            note.hidden = true;
            note.textContent = '';
            return;
        }
        note.hidden = false;
        note.replaceChildren();
        parsePartyFundsEntries(noteText).forEach((entry) => {
            const item = document.createElement('li');
            item.className = 'party-funds-entry';
            const date = document.createElement('strong');
            date.className = 'party-funds-entry-date';
            date.textContent = entry.date;
            const detail = document.createElement('span');
            detail.className = 'party-funds-entry-detail';
            detail.textContent = entry.text;
            item.append(date, detail);
            note.append(item);
        });
    };

    const validateMessageSnapshot = (payload) => {
        const validRecipient = (recipient) => recipient
            && typeof recipient.account_id === 'string'
            && /^[a-f0-9]{32}$/u.test(recipient.account_id)
            && typeof recipient.character_name === 'string'
            && recipient.character_name.trim().length >= 1
            && recipient.character_name.length <= 100;
        const validMessage = (message) => message
            && typeof message.id === 'string'
            && /^[a-f0-9]{32}$/u.test(message.id)
            && typeof message.sender_character_name === 'string'
            && message.sender_character_name.trim().length >= 1
            && message.sender_character_name.length <= 100
            && typeof message.recipient_character_name === 'string'
            && message.recipient_character_name.trim().length >= 1
            && message.recipient_character_name.length <= 100
            && typeof message.message === 'string'
            && message.message.trim().length >= 1
            && message.message.length <= 5000
            && typeof message.sent_at === 'string'
            && Number.isFinite(Date.parse(message.sent_at))
            && message.read_at === null;
        if (!payload
            || payload.schema_version !== 3
            || !Array.isArray(payload.messages)
            || payload.messages.length > 100
            || !payload.messages.every(validMessage)
            || !Number.isInteger(payload.unread_count)
            || payload.unread_count < payload.messages.length
            || payload.unread_count > 1000000
            || !(payload.next_cursor === null
                || (typeof payload.next_cursor === 'string'
                    && /^[A-Za-z0-9_-]{1,256}$/u.test(payload.next_cursor)))
            || !Array.isArray(payload.player_recipients)
            || payload.player_recipients.length > 200
            || !payload.player_recipients.every(validRecipient)) {
            throw new Error('The message service returned an invalid response.');
        }
        assertScopedMessages(payload.messages, authenticatedAccount?.id ?? '');
        return payload;
    };

    const formatMessageDate = (value) => new Intl.DateTimeFormat(
        undefined,
        { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value));

    const validateRevisionSnapshot = (payload) => {
        const validRevision = (entry, requireUnread) => entry
            && typeof entry.revision === 'string'
            && /^[a-f0-9]{64}$/u.test(entry.revision)
            && Number.isInteger(entry.activity_count)
            && entry.activity_count >= 0
            && entry.activity_count <= 1000000
            && (!requireUnread
                || (Number.isInteger(entry.unread_count)
                    && entry.unread_count === entry.activity_count));
        if (!payload
            || payload.schema_version !== 1
            || typeof payload.observed_at !== 'string'
            || !Number.isFinite(Date.parse(payload.observed_at))
            || !validRevision(payload.messages, true)
            || !validRevision(payload.quests, false)) {
            throw new Error('The activity revision service returned an invalid response.');
        }
        return payload;
    };

    const renderActivityUi = () => {
        const nav = byId('activity-nav');
        const navCount = byId('activity-nav-count');
        const status = byId('activity-status');
        const list = byId('activity-list');
        const messages = authenticatedMessageSnapshot?.messages || [];
        const questActivity = authenticatedAccount?.role === 'dm'
            ? (authenticatedQuestSnapshot?.pending_requests || [])
            : (authenticatedQuestSnapshot?.notifications || []);
        const total = (authenticatedRevisionSnapshot?.messages.activity_count ?? messages.length)
            + (authenticatedRevisionSnapshot?.quests.activity_count ?? questActivity.length);
        if (nav instanceof HTMLButtonElement) nav.hidden = authenticatedAccount === null;
        if (navCount) {
            navCount.hidden = total === 0;
            navCount.textContent = total > 99 ? '99+' : String(total);
        }
        renderFreshness('activity-freshness', authenticatedAccount === null ? 0 : revisionsUpdatedAt);
        list?.replaceChildren();
        if (list) list.hidden = true;
        if (authenticatedAccount === null) {
            if (status) status.textContent = 'Log in to view campaign activity.';
            return;
        }
        if (status) status.textContent = total === 0
            ? 'You are caught up. New messages and quest decisions will appear here.'
            : `${total} active inbox item${total === 1 ? '' : 's'}.`;
        if (!list || (messages.length === 0 && questActivity.length === 0)) return;
        const fragment = document.createDocumentFragment();
        messages.forEach((message) => {
            const card = document.createElement('article');
            card.className = 'quest-alert-card';
            const heading = document.createElement('h3');
            heading.textContent = `Message from ${message.sender_character_name}`;
            const body = document.createElement('p');
            body.textContent = message.message;
            const meta = document.createElement('p');
            meta.className = 'message-notification-meta';
            meta.textContent = formatMessageDate(message.sent_at);
            card.append(heading, body, meta);
            fragment.append(card);
        });
        questActivity.forEach((request) => {
            const card = document.createElement('article');
            card.className = 'quest-alert-card';
            const heading = document.createElement('h3');
            heading.textContent = authenticatedAccount.role === 'dm'
                ? `Quest request from ${request.requester_character_name}`
                : `Quest request ${request.status}`;
            const body = document.createElement('p');
            body.textContent = request.quest_title;
            card.append(heading, body);
            fragment.append(card);
        });
        list.append(fragment);
        list.hidden = false;
    };

    const loadRevisions = async () => {
        const requestId = ++revisionRequestId;
        if (authenticatedAccount === null || document.hidden || !navigator.onLine) return;
        const accountId = authenticatedAccount.id;
        try {
            const snapshot = validateRevisionSnapshot(await requestAuthenticationApi('/revisions'));
            if (requestId !== revisionRequestId || authenticatedAccount?.id !== accountId) return;
            authenticatedRevisionSnapshot = snapshot;
            revisionsUpdatedAt = Date.now();
            renderActivityUi();
            const refreshes = [];
            if (appliedMessageRevision !== snapshot.messages.revision) {
                refreshes.push(loadMessages().then((succeeded) => {
                    if (succeeded) appliedMessageRevision = snapshot.messages.revision;
                }));
            }
            if (appliedQuestRevision !== snapshot.quests.revision) {
                refreshes.push(loadQuests().then((succeeded) => {
                    if (succeeded) appliedQuestRevision = snapshot.quests.revision;
                }));
            }
            await Promise.all(refreshes);
            if (authenticatedAccount?.role === 'dm'
                && activeView === 'dashboard'
                && authenticatedPresenceSnapshot !== null
                && !document.hidden
                && navigator.onLine) {
                await loadPresence();
            }
            renderActivityUi();
        } catch (error) {
            if (requestId !== revisionRequestId || authenticatedAccount?.id !== accountId) return;
            if (activeView === 'activity') {
                const status = byId('activity-status');
                if (status) status.textContent = error.message;
            }
        }
    };

    const RESOURCE_BUDGET_PWA_POLLING_SECONDS = 30;

    const updateRevisionPolling = () => {
        if (revisionPollTimer !== 0) {
            window.clearInterval(revisionPollTimer);
            revisionPollTimer = 0;
        }
        revisionRequestId++;
        if (authenticatedAccount === null || document.hidden || !navigator.onLine) return;
        void loadRevisions();
        revisionPollTimer = window.setInterval(() => void loadRevisions(), RESOURCE_BUDGET_PWA_POLLING_SECONDS * 1000);
    };

    const renderMessageNotifications = () => {
        const button = byId('message-notification-button');
        const count = byId('message-notification-count');
        const dialog = byId('message-notification-dialog');
        const summary = byId('message-notification-summary');
        const list = byId('message-notification-list');
        const messages = authenticatedMessageSnapshot?.messages || [];
        const unreadCount = authenticatedMessageSnapshot?.unread_count || 0;
        const nextButton = byId('messages-next');
        renderFreshness('messages-freshness', authenticatedAccount === null ? 0 : messagesUpdatedAt);
        const showNotification = authenticatedAccount !== null && unreadCount > 0;

        if (button instanceof HTMLButtonElement) {
            button.hidden = !showNotification;
            button.setAttribute(
                'aria-label',
                `${unreadCount} unread message${unreadCount === 1 ? '' : 's'}`);
            button.title = button.getAttribute('aria-label') || 'Unread messages';
        }
        if (count) count.textContent = unreadCount > 99 ? '99+' : String(unreadCount);
        if (nextButton instanceof HTMLButtonElement) {
            nextButton.hidden = typeof authenticatedMessageSnapshot?.next_cursor !== 'string';
            nextButton.disabled = messageLoading;
        }
        if (!(dialog instanceof HTMLDialogElement) || list === null) return;

        list.replaceChildren();
        if (!showNotification) {
            if (dialog.open) dialog.close();
            return;
        }
        if (summary) {
            summary.textContent = `${unreadCount} unread message${unreadCount === 1 ? '' : 's'}.`;
        }

        const fragment = document.createDocumentFragment();
        messages.forEach((message) => {
            const card = document.createElement('article');
            card.className = 'message-notification';

            const heading = document.createElement('h3');
            heading.textContent = `From ${message.sender_character_name}`;

            const meta = document.createElement('p');
            meta.className = 'message-notification-meta';
            meta.textContent = formatMessageDate(message.sent_at);

            const body = document.createElement('p');
            body.className = 'message-notification-message';
            body.textContent = message.message;

            const actions = document.createElement('div');
            actions.className = 'quest-alert-actions';
            const readButton = document.createElement('button');
            readButton.className = 'secondary-button';
            readButton.type = 'button';
            readButton.textContent = 'Mark as read';
            readButton.addEventListener('click', () => {
                void markMessageRead(message.id, readButton);
            });
            actions.append(readButton);
            card.append(heading, meta, body, actions);
            fragment.append(card);
        });
        list.append(fragment);
    };

    const loadMessages = async (cursor = null) => {
        const requestId = ++messageRequestId;
        if (authenticatedAccount === null) {
            authenticatedMessageSnapshot = null;
            messageLoading = false;
            messageError = '';
            renderMessageNotifications();
            return false;
        }
        const accountId = authenticatedAccount.id;
        messageLoading = true;
        messageError = '';
        try {
            const snapshot = validateMessageSnapshot(await requestAuthenticationApi(
                cursor === null ? '/messages?limit=50' : `/messages?limit=50&cursor=${encodeURIComponent(cursor)}`));
            if (requestId !== messageRequestId || authenticatedAccount?.id !== accountId) return false;
            authenticatedMessageSnapshot = mergeInboxSnapshot(
                authenticatedMessageSnapshot, snapshot, cursor);
            messagesUpdatedAt = Date.now();
            updateAuthenticationUi();
            renderActivityUi();
            return true;
        } catch (error) {
            if (requestId !== messageRequestId || authenticatedAccount?.id !== accountId) return false;
            if (cursor === null) authenticatedMessageSnapshot = null;
            messageError = error.message;
            return false;
        } finally {
            if (requestId === messageRequestId && authenticatedAccount?.id === accountId) {
                messageLoading = false;
                renderMessageNotifications();
                renderActivityUi();
            }
        }
    };

    const markMessageRead = async (messageId, button) => {
        if (!(button instanceof HTMLButtonElement) || authenticatedAccount === null) return;
        button.disabled = true;
        const summary = byId('message-notification-summary');
        if (summary) summary.textContent = 'Marking message as read...';
        try {
            await requestAuthenticationApi(
                `/messages/${messageId}/read`,
                { method: 'POST', body: {}, csrf: true });
            authenticatedRevisionSnapshot = null;
            await Promise.all([loadMessages(), loadRevisions()]);
        } catch (error) {
            messageError = error.message;
            if (summary) summary.textContent = messageError;
            button.disabled = false;
        }
    };

    const messagesActivityController = createMessagesActivityController({
        load: () => loadMessages()
    });

    const isMessageTextReady = (value) => String(value || '').trim().length >= 3;

    const updateMessageDmSubmitState = () => {
        const messageInput = byId('message-dm-text');
        const submitButton = byId('message-dm-submit');
        if (!(messageInput instanceof HTMLTextAreaElement) || !(submitButton instanceof HTMLButtonElement)) return;
        submitButton.disabled = !isMessageTextReady(messageInput.value);
    };

    const updateMessagePlayerSubmitState = () => {
        const messageInput = byId('message-player-text');
        const submitButton = byId('message-player-submit');
        const recipientSelect = byId('message-player-recipient');
        if (
            !(messageInput instanceof HTMLTextAreaElement)
            || !(submitButton instanceof HTMLButtonElement)
            || !(recipientSelect instanceof HTMLSelectElement)
        ) return;
        const recipient = recipientSelect.value;
        submitButton.disabled = !(
            isMessageTextReady(messageInput.value)
            && (/^[a-f0-9]{32}$/u.test(recipient)
                || (authenticatedAccount?.role === 'dm' && recipient === 'all-players'))
        );
    };

    const renderMessageDmUi = () => {
        const input = byId('message-dm-text');
        if (input instanceof HTMLTextAreaElement && messageDraftStore) input.value = messageDraftStore.load();
        updateMessageDmSubmitState();
        const status = byId('message-dm-status');
        if (status) status.hidden = true;
    };

    const renderMessagePlayerRecipients = () => {
        const recipientSelect = byId('message-player-recipient');
        const submitButton = byId('message-player-submit');
        const status = byId('message-player-status');
        if (!(recipientSelect instanceof HTMLSelectElement)) return;
        recipientSelect.replaceChildren();

        if (authenticatedMessageSnapshot === null) {
            const loadingOption = document.createElement('option');
            loadingOption.value = '';
            loadingOption.textContent = 'Loading players…';
            loadingOption.disabled = true;
            loadingOption.selected = true;
            recipientSelect.append(loadingOption);
            recipientSelect.disabled = true;
            if (submitButton instanceof HTMLButtonElement) submitButton.disabled = true;
            if (status) {
                status.hidden = true;
            }
            return;
        }

        const players = authenticatedMessageSnapshot.player_recipients;
        if (players.length === 0) {
            const noneOption = document.createElement('option');
            noneOption.value = '';
            noneOption.textContent = 'No available players';
            noneOption.disabled = true;
            noneOption.selected = true;
            recipientSelect.append(noneOption);
            recipientSelect.disabled = true;
            if (submitButton instanceof HTMLButtonElement) submitButton.disabled = true;
            if (status) {
                status.hidden = false;
                status.textContent = 'No online user list is available.';
            }
            return;
        }

        const defaultOption = document.createElement('option');
        defaultOption.value = '';
        defaultOption.textContent = 'Select a player';
        defaultOption.selected = true;
        defaultOption.disabled = true;
        recipientSelect.append(defaultOption);

        if (authenticatedAccount?.role === 'dm') {
            const everyPlayerOption = document.createElement('option');
            everyPlayerOption.value = 'all-players';
            everyPlayerOption.textContent = 'Every player';
            recipientSelect.append(everyPlayerOption);
        }

        players.forEach((user) => {
            const option = document.createElement('option');
            option.value = user.account_id;
            option.textContent = user.character_name;
            recipientSelect.append(option);
        });
        recipientSelect.disabled = false;
        if (submitButton instanceof HTMLButtonElement) submitButton.disabled = true;
        if (status) status.hidden = true;
    };

    const renderMessagePlayerUi = () => {
        const input = byId('message-player-text');
        if (input instanceof HTMLTextAreaElement && messageDraftStore) input.value = messageDraftStore.load();
        renderMessagePlayerRecipients();
        updateMessagePlayerSubmitState();
        const status = byId('message-player-status');
        if (!status) return;
        const usersAvailable = (authenticatedMessageSnapshot?.player_recipients.length || 0) > 0;
        if (usersAvailable) {
            status.hidden = true;
            status.textContent = '';
        }
    };

    const loadPartyFunds = async (force = false) => {
        if (partyFundsLoading && !force) return partyFundsLoading;
        partyFundsSnapshot = null;
        partyFundsError = '';
        renderPartyFunds();
        const refreshButton = byId('party-funds-refresh');
        if (refreshButton instanceof HTMLButtonElement) refreshButton.disabled = true;
        partyFundsLoading = (async () => {
            try {
                partyFundsSnapshot = await fetchPartyFunds();
                partyFundsUpdatedAt = Date.now();
            } catch {
                partyFundsError = 'Party-funds information is unavailable from the bundled file.';
            } finally {
                partyFundsLoading = null;
                if (refreshButton instanceof HTMLButtonElement) refreshButton.disabled = false;
                renderPartyFunds();
            }
        })();
        renderPartyFunds();
        if (partyFundsLoading) {
            void partyFundsLoading.finally(() => renderPartyFunds());
        }
        return partyFundsLoading;
    };

    byId('party-funds-refresh')?.addEventListener('click', () => {
        void loadPartyFunds(true);
    });

    byId('message-dm-text')?.addEventListener('input', () => {
        const input = byId('message-dm-text');
        if (input instanceof HTMLTextAreaElement) messageDraftStore?.save(input.value);
        updateMessageDmSubmitState();
    });
    byId('message-dm-text')?.addEventListener('change', () => {
        updateMessageDmSubmitState();
    });
    byId('message-dm-submit')?.addEventListener('click', async () => {
        const messageInput = byId('message-dm-text');
        const submitButton = byId('message-dm-submit');
        const status = byId('message-dm-status');
        if (!(messageInput instanceof HTMLTextAreaElement)
            || !(submitButton instanceof HTMLButtonElement)) {
            return;
        }
        const message = messageInput.value;
        if (!isMessageTextReady(message)) {
            submitButton.disabled = true;
            return;
        }
        submitButton.disabled = true;
        if (status) {
            status.hidden = false;
            status.textContent = 'Sending message to the Dungeon Master...';
        }
        try {
            await requestAuthenticationApi('/messages', {
                method: 'POST',
                body: {
                    recipient_role: 'dm',
                    message
                },
                csrf: true
            });
            if (status) {
                status.textContent = 'Your message was sent to the Dungeon Master.';
            }
            messageInput.value = '';
            messageDraftStore?.clear();
            setTimeout(() => {
                if (status) status.hidden = true;
            }, 2500);
        } catch (error) {
            if (status) {
                status.textContent = error.message;
            }
        } finally {
            updateMessageDmSubmitState();
        }
    });

    byId('message-player-recipient')?.addEventListener('change', () => {
        updateMessagePlayerSubmitState();
    });
    byId('message-player-text')?.addEventListener('input', () => {
        const input = byId('message-player-text');
        if (input instanceof HTMLTextAreaElement) messageDraftStore?.save(input.value);
        updateMessagePlayerSubmitState();
    });
    byId('message-player-text')?.addEventListener('change', () => {
        updateMessagePlayerSubmitState();
    });
    byId('message-player-submit')?.addEventListener('click', async () => {
        const messageInput = byId('message-player-text');
        const recipientSelect = byId('message-player-recipient');
        const submitButton = byId('message-player-submit');
        const status = byId('message-player-status');
        if (!(messageInput instanceof HTMLTextAreaElement)
            || !(recipientSelect instanceof HTMLSelectElement)
            || !(submitButton instanceof HTMLButtonElement)) {
            return;
        }
        const message = messageInput.value;
        const recipient = recipientSelect.value;
        const isEveryPlayer = authenticatedAccount?.role === 'dm' && recipient === 'all-players';
        if (!isMessageTextReady(message)
            || (!isEveryPlayer && !/^[a-f0-9]{32}$/u.test(recipient))) {
            submitButton.disabled = true;
            return;
        }
        submitButton.disabled = true;
        if (status) {
            const recipientLabel = (recipientSelect.selectedOptions[0] instanceof HTMLOptionElement)
                ? recipientSelect.selectedOptions[0].textContent
                : 'selected player';
            status.hidden = false;
            status.textContent = `Sending message to ${recipientLabel}...`;
        }
        try {
            await requestAuthenticationApi('/messages', {
                method: 'POST',
                body: {
                    ...(isEveryPlayer
                        ? { recipient_role: 'all_players' }
                        : { recipient_account_id: recipient }),
                    message
                },
                csrf: true
            });
            if (status) {
                const recipientLabel = (recipientSelect.selectedOptions[0] instanceof HTMLOptionElement)
                    ? recipientSelect.selectedOptions[0].textContent
                    : 'the selected player';
                status.textContent = `Your message was sent to ${recipientLabel}.`;
            }
            messageInput.value = '';
            recipientSelect.value = '';
            updateMessagePlayerSubmitState();
            setTimeout(() => {
                if (status) status.hidden = true;
            }, 2500);
        } catch (error) {
            if (status) {
                status.textContent = error.message;
            }
            updateMessagePlayerSubmitState();
        }
    });

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
        const requestId = ++magicItemRequestId;
        const requestGeneration = authenticationGeneration;
        const accountId = authenticatedAccount?.id || '';
        if (accountId === '') {
            resetMagicItemState();
            renderMagicItems();
            return null;
        }
        magicItemSnapshot = null;
        magicItemError = '';
        renderMagicItems();
        const refreshButton = byId('magic-items-refresh');
        if (refreshButton instanceof HTMLButtonElement) refreshButton.disabled = true;
        let loadingPromise;
        const isCurrentRequest = () => requestId === magicItemRequestId
            && requestGeneration === authenticationGeneration
            && authenticatedAccount?.id === accountId;
        loadingPromise = (async () => {
            try {
                if (!isCurrentRequest()) return;
                magicItemSnapshot = {
                    ...(await fetchBrokerMagicItems()),
                    data_source: 'broker'
                };
                if (!isCurrentRequest()) return;
            } catch {
                if (!isCurrentRequest()) return;
                try {
                    magicItemSnapshot = {
                        ...(await fetchFallbackMagicItems()),
                        data_source: 'fallback'
                    };
                    if (!isCurrentRequest()) return;
                } catch {
                    if (isCurrentRequest()) {
                        magicItemError = 'Magic-item information is unavailable from both the campaign wiki and the bundled fallback.';
                    }
                }
            } finally {
                if (isCurrentRequest() && magicItemLoading === loadingPromise) {
                    if (magicItemSnapshot !== null) magicItemsUpdatedAt = Date.now();
                    magicItemLoading = null;
                    if (refreshButton instanceof HTMLButtonElement) refreshButton.disabled = false;
                    renderMagicItems();
                }
            }
        })();
        magicItemLoading = loadingPromise;
        renderMagicItems();
        return magicItemLoading;
    };

    byId('magic-items-refresh')?.addEventListener('click', () => {
        void loadMagicItems(true);
    });

    const updateAuthenticationUi = () => {
        const authenticated = authenticatedAccount !== null;
        const isDungeonMaster = authenticatedAccount?.role === 'dm';
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
        const messageDmNavButton = byId('message-dm-nav');
        if (messageDmNavButton) {
            messageDmNavButton.hidden = !authenticated || isDungeonMaster;
        }
        const messagePlayerNavButton = byId('message-player-nav');
        const canMessagePlayer = authenticated
            && (isDungeonMaster
                || (authenticatedMessageSnapshot?.player_recipients.length || 0) > 0);
        if (messagePlayerNavButton) {
            messagePlayerNavButton.hidden = !canMessagePlayer;
        }
        for (const button of navButtons) {
            const targetView = button.dataset.view;
            if (!protectedNavViews.has(targetView)) continue;
            button.hidden = targetView === 'xp-awards'
                ? !canViewXpAwards(authenticatedAccount)
                : !authenticated;
        }
        if (isDungeonMaster && activeView === 'message-dm') {
            setView('dashboard', false);
        }
        if (!canMessagePlayer && activeView === 'message-player') {
            setView('dashboard', false);
        }
        if (!authenticated && protectedNavViews.has(activeView)) {
            setView('dashboard', false);
        }
        if (!canViewXpAwards(authenticatedAccount) && activeView === 'xp-awards') {
            setView('dashboard', false);
        }
        const requestedPublicView = location.hash.slice(1);
        if (!authenticated && requestedPublicView && !protectedNavViews.has(requestedPublicView)
            && views.has(requestedPublicView) && activeView !== requestedPublicView) {
            setView(requestedPublicView, false);
        }
        const protectedStatus = byId('protected-player-status');
        if (protectedStatus) {
            protectedStatus.textContent = authenticated
                ? `Signed in as ${authenticatedAccount.character_name}. Protected requests are authorized from this server session.`
                : 'Log in with your character name and password. The server determines which character record the session may access; passwords and private records are never embedded in this browser application.';
        }
        renderAuthenticatedHeroToken();
        renderXpUi();
        renderXpAwardsUi();
        renderWordCountUi();
        renderQuestUi();
        renderMagicItems();
        renderPartyFunds();
        renderMessageDmUi();
        renderMessagePlayerUi();
        renderMessageNotifications();
        renderActivityUi();
        updatePresencePolling();
        if (authenticated) {
            if (activeView === 'magic-items' && magicItemSnapshot === null) void loadMagicItems();
            if (activeView === 'party-funds' && partyFundsSnapshot === null) void loadPartyFunds();
        }
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

    class AuthenticationApiError extends Error {
        constructor(message, details = {}) {
            super(message);
            this.name = 'AuthenticationApiError';
            this.code = typeof details.code === 'string' ? details.code : 'api_error';
            this.status = Number.isInteger(details.status) ? details.status : 0;
            this.requestId = typeof details.requestId === 'string' ? details.requestId : '';
            this.retryable = details.retryable === true;
            this.expired = details.expired === true;
        }
    }

    const createApiRequestId = () => {
        if (globalThis.crypto?.randomUUID) return globalThis.crypto.randomUUID();
        const bytes = new Uint8Array(16);
        globalThis.crypto?.getRandomValues?.(bytes);
        return [...bytes].map((value) => value.toString(16).padStart(2, '0')).join('');
    };

    const cancelAuthenticationRequests = (except = null) => {
        for (const controller of activeAuthenticationControllers) {
            if (controller !== except) controller.abort();
        }
        if (except === null) activeAuthenticationControllers.clear();
        else activeAuthenticationControllers.delete(except);
    };

    const accountSessionController = createAccountSessionController({
        onChange: (account) => {
            if (authenticatedAccount?.id && authenticatedAccount.id !== account?.id) {
                offlineActionQueue.clearForIdentity(authenticatedAccount.id);
            }
            const changed = authenticatedAccount?.id !== account?.id;
            authenticatedAccount = account;
            if (!changed) return;
            authenticationCsrfToken = '';
            clearProtectedFreshness();
            resetMagicItemState();
            authenticatedXpSnapshot = null;
            authenticatedXpAwardsSnapshot = null;
            xpAwardsLoading = null;
            xpAwardsError = '';
            authenticatedWordCountSnapshot = null;
            authenticatedPresenceSnapshot = null;
            authenticatedQuestSnapshot = null;
            authenticatedMessageSnapshot = null;
            authenticatedRevisionSnapshot = null;
            messageDraftStore?.clear();
            messageDraftStore = null;
            document.querySelectorAll('#message-dm-text, #message-player-text').forEach((input) => { input.value = ''; });
            document.querySelectorAll('[data-protected-content]').forEach((element) => { element.replaceChildren(); });
        }
    });

    const beginAuthenticationGeneration = (except = null) => {
        authenticationGeneration++;
        seenProtectedResponseNonces.clear();
        authenticatedResourceGeneration = '';
        authenticatedAbsoluteExpiresAt = 0;
        accountSessionController.beginTransition();
        cancelAuthenticationRequests(except);
    };

    const clearExpiredAuthentication = (exceptController = null) => {
        if (authenticatedAccount === null) return;
        beginAuthenticationGeneration(exceptController);
        authenticatedAccount = null;
        authenticationCsrfToken = '';
        clearProtectedFreshness();
        resetMagicItemState();
        authenticatedXpSnapshot = null;
        xpRequestId++;
        authenticatedXpAwardsSnapshot = null;
        xpAwardsLoading = null;
        xpAwardsError = '';
        resetLevelUpNotificationState();
        xpAwardsRequestId++;
        authenticatedWordCountSnapshot = null;
        wordCountRequestId++;
        authenticatedPresenceSnapshot = null;
        presenceRequestId++;
        authenticatedQuestSnapshot = null;
        questRequestId++;
        authenticatedMessageSnapshot = null;
        messageRequestId++;
        authenticatedRevisionSnapshot = null;
        appliedMessageRevision = null;
        appliedQuestRevision = null;
        revisionRequestId++;
        messageLoading = false;
        messageError = '';
        questStateFilter = '';
        lastQuestAlertSignature = '';
        for (const id of ['level-up-alert-dialog', 'quest-alert-dialog', 'message-notification-dialog']) {
            const dialog = byId(id);
            if (dialog instanceof HTMLDialogElement && dialog.open) dialog.close();
        }
        setAuthenticationMessage('Your session expired. Log in again.', true, true);
        updateAuthenticationUi();
        updateRevisionPolling();
    };

    const offlineActionQueue = createOfflineActionQueue({
        onState: ({ state, reason }) => {
            const status = byId('connection-status');
            if (!(status instanceof HTMLElement)) return;
            status.dataset.queueState = state;
            status.title = state === 'completed'
                ? 'Queued action completed after reconnecting.'
                : state === QUEUE_STATES.CONFLICT
                    ? `Queued action needs review: ${reason}.`
                    : state === QUEUE_STATES.EXHAUSTED
                        ? 'A queued action could not be delivered after the retry limit.'
                        : state === QUEUE_STATES.QUEUED ? 'An action is queued until the connection returns.' : '';
            const label = byId('connection-label');
            if (label && [QUEUE_STATES.QUEUED, QUEUE_STATES.CONFLICT, QUEUE_STATES.EXHAUSTED].includes(state)) {
                label.textContent = state === QUEUE_STATES.CONFLICT ? 'Action conflict' : state === QUEUE_STATES.EXHAUSTED ? 'Action failed' : 'Action queued';
            }
        }
    });
    const renderQueuedActionState = () => {
        const label = byId('connection-label');
        if (!(label instanceof HTMLElement)) return;
        const pending = offlineActionQueue.list().filter((item) => [QUEUE_STATES.QUEUED, QUEUE_STATES.CONFLICT, QUEUE_STATES.EXHAUSTED].includes(item.state));
        label.textContent = pending.some((item) => item.state === QUEUE_STATES.CONFLICT)
            ? 'Action conflict'
            : pending.some((item) => item.state === QUEUE_STATES.EXHAUSTED)
                ? 'Action failed'
                : pending.length > 0 ? 'Action queued' : navigator.onLine ? 'Online' : 'Offline';
    };
    renderQueuedActionState();

    const PROTECTED_RESPONSE_TRUST = Object.freeze({
        algorithm: 'Ed25519',
        keyId: 'protected-prod-2026',
        publicKey: 'ZN3EvmPpN0r7dtWqybDnB6zhGWBrNCPFIuDi8J1BQLk='
    });
    const canonicalProtectedValue = (value) => {
        if (Array.isArray(value)) return value.map(canonicalProtectedValue);
        if (value && typeof value === 'object') {
            return Object.fromEntries(Object.keys(value).sort().map((key) => [key, canonicalProtectedValue(value[key])]));
        }
        return value;
    };
    const digestProtectedBody = async (value) => {
        const bytes = new TextEncoder().encode(JSON.stringify(canonicalProtectedValue(value)));
        const digest = await crypto.subtle.digest('SHA-256', bytes);
        return [...new Uint8Array(digest)].map((byte) => byte.toString(16).padStart(2, '0')).join('');
    };
    const verifyProtectedEnvelope = async (payload, meta, method, route) => {
        if (meta?.algorithm !== PROTECTED_RESPONSE_TRUST.algorithm || meta.key_id !== PROTECTED_RESPONSE_TRUST.keyId
            || meta.method !== method || meta.route !== route || meta.schema_version !== 2
            || meta.body_digest !== await digestProtectedBody(Object.fromEntries(Object.entries(payload).filter(([key]) => key !== '_protected_resource')))) return false;
        const signed = Object.fromEntries(Object.entries(meta).filter(([key]) => key !== 'signature'));
        const keyBytes = Uint8Array.from(atob(PROTECTED_RESPONSE_TRUST.publicKey), (char) => char.charCodeAt(0));
        const signature = Uint8Array.from(atob(meta.signature || ''), (char) => char.charCodeAt(0));
        const key = await crypto.subtle.importKey('raw', keyBytes, { name: 'Ed25519' }, false, ['verify']);
        return crypto.subtle.verify({ name: 'Ed25519' }, key, signature,
            new TextEncoder().encode(JSON.stringify(canonicalProtectedValue(signed))));
    };

    const requestAuthenticationApi = async (path, options = {}) => {
        const method = String(options.method || 'GET').toUpperCase();
        const requestGeneration = authenticationGeneration;
        const requestId = typeof options.requestId === 'string' && options.requestId !== ''
            ? options.requestId
            : createApiRequestId();
        const headers = new Headers({
            Accept: 'application/json',
            'X-Request-Id': requestId,
            ...CORRELATION_HEADERS
        });
        if (options.body !== undefined) headers.set('Content-Type', 'application/json');
        if (options.csrf === true && authenticationCsrfToken) {
            headers.set('X-CSRF-Token', authenticationCsrfToken);
        }
        if (!['GET', 'HEAD', 'OPTIONS'].includes(method)
            && path !== '/login'
            && path !== '/logout') {
            headers.set(
                'Idempotency-Key',
                typeof options.idempotencyKey === 'string' && options.idempotencyKey !== ''
                    ? options.idempotencyKey
                    : createApiRequestId());
        }
        const controller = new AbortController();
        let timedOut = false;
        const timeoutId = window.setTimeout(() => {
            timedOut = true;
            controller.abort();
        }, AUTH_REQUEST_TIMEOUT_MS);
        let externalAbortHandler = null;
        if (options.signal instanceof AbortSignal) {
            externalAbortHandler = () => controller.abort();
            if (options.signal.aborted) controller.abort();
            else options.signal.addEventListener('abort', externalAbortHandler, { once: true });
        }
        activeAuthenticationControllers.add(controller);
        try {
            let response;
            try {
                response = await fetch(`${AUTH_API_ROOT}${path}`, {
                    method,
                    headers,
                    body: options.body === undefined ? undefined : JSON.stringify(options.body),
                    credentials: 'same-origin',
                    cache: 'no-store',
                    redirect: 'error',
                    signal: controller.signal
                });
            } catch (error) {
                const cancelled = controller.signal.aborted;
                const apiError = new AuthenticationApiError(
                    timedOut
                        ? 'The character login request timed out.'
                        : cancelled ? 'The character login request was cancelled.' : 'The character login service is unavailable.',
                    {
                        code: timedOut ? 'request_timeout' : cancelled ? 'request_cancelled' : 'network_error',
                        requestId,
                        retryable: true
                    });
                const queueable = options.allowQueue !== false
                    && MUTATING_METHODS.has(method)
                    && !['/login', '/logout'].includes(path)
                    && authenticatedAccount !== null
                    && !cancelled
                    && (apiError.code === 'network_error' || apiError.code === 'request_timeout');
                if (queueable) {
                    const idempotencyKey = headers.get('Idempotency-Key');
                    const queued = offlineActionQueue.enqueue({
                        accountId: authenticatedAccount.id,
                        generation: String(authenticationGeneration),
                        method,
                        route: path.split('?')[0],
                        idempotencyKey,
                        body: options.body === undefined ? null : options.body
                    });
                    return {
                        schema_version: 1,
                        queued: true,
                        queue_state: queued.state,
                        request_id: requestId
                    };
                }
                throw apiError;
            }
            const responseRequestId = response.headers.get('X-Request-Id') || requestId;
            if (response.status === 401 && path !== '/login') {
                // Invalidate immediately: a broken or captive-portal body must not delay cleanup.
                const isCurrentGeneration = requestGeneration === authenticationGeneration;
                if (isCurrentGeneration) clearExpiredAuthentication(controller);
                throw new AuthenticationApiError(
                    'Authentication required.',
                    {
                        code: 'authentication_required',
                        status: 401,
                        requestId: responseRequestId,
                        expired: isCurrentGeneration,
                        retryable: false
                    });
            }
            if (requestGeneration !== authenticationGeneration && path !== '/login') {
                throw new AuthenticationApiError(
                    'The character login response was superseded by an account change.',
                    { code: 'stale_generation', requestId: responseRequestId, retryable: true });
            }
            let payload = {};
            try {
                payload = await response.json();
            } catch {
                throw new AuthenticationApiError(
                    response.ok
                        ? 'The character login service returned an invalid response.'
                        : 'The character login request failed.',
                    {
                        code: 'invalid_response',
                        status: response.status,
                        requestId: responseRequestId,
                        retryable: response.status >= 500
                    });
            }
            if (requestGeneration !== authenticationGeneration && path !== '/login') {
                throw new AuthenticationApiError(
                    'The character login response was superseded by an account change.',
                    { code: 'stale_generation', requestId: responseRequestId, retryable: true });
            }
            if (path !== '/login' && path !== '/logout' && response.ok) {
                const protectedResource = payload?._protected_resource;
                const expiresAt = protectedResource && Date.parse(protectedResource.expires_at);
                const issuedAt = protectedResource && Date.parse(protectedResource.issued_at);
                const nonce = protectedResource?.nonce;
                const now = Date.now();
                if (!protectedResource
                    || !(await verifyProtectedEnvelope(payload, protectedResource, method, `/v1${path}`))
                    || protectedResource.account_id !== authenticatedAccount?.id
                    || protectedResource.generation !== authenticatedResourceGeneration
                    || !/^[a-f0-9]{64}$/u.test(String(protectedResource.generation || ''))
                    || !/^[a-f0-9]{64}$/u.test(String(protectedResource.body_digest || ''))
                    || !/^[a-f0-9]{32}$/u.test(String(nonce || ''))
                    || !Number.isFinite(issuedAt)
                    || !Number.isFinite(expiresAt)
                    || issuedAt > now
                    || expiresAt <= now
                    || expiresAt - issuedAt > 300000
                    || (Number.isFinite(authenticatedAbsoluteExpiresAt)
                        && expiresAt > authenticatedAbsoluteExpiresAt)
                    || seenProtectedResponseNonces.has(nonce)) {
                    throw new AuthenticationApiError(
                        'The protected response was stale, replayed, or bound to another account.',
                        { code: 'protected_response_rejected', requestId: responseRequestId, retryable: true });
                }
                seenProtectedResponseNonces.add(nonce);
                if (seenProtectedResponseNonces.size > 512) {
                    seenProtectedResponseNonces.delete(seenProtectedResponseNonces.values().next().value);
                }
            }
            if (!response.ok) {
                throw new AuthenticationApiError(
                    typeof payload.message === 'string' && payload.message !== ''
                        ? payload.message
                        : 'The character login request failed.',
                    {
                        code: typeof payload.error === 'string' ? payload.error : 'api_error',
                        status: response.status,
                        requestId: typeof payload.request_id === 'string' ? payload.request_id : responseRequestId,
                        retryable: response.status >= 500 || response.status === 429
                    });
            }
            if (payload && typeof payload === 'object' && !Array.isArray(payload)) {
                payload.request_id = typeof payload.request_id === 'string'
                    ? payload.request_id
                    : responseRequestId;
            }
            return payload;
        } finally {
            window.clearTimeout(timeoutId);
            activeAuthenticationControllers.delete(controller);
            if (externalAbortHandler && options.signal instanceof AbortSignal) {
                options.signal.removeEventListener('abort', externalAbortHandler);
            }
        }
    };

    const flushQueuedActions = async () => {
        if (!navigator.onLine || authenticatedAccount === null) return 0;
        return offlineActionQueue.flush({
            accountId: authenticatedAccount.id,
            generation: String(authenticationGeneration),
            send: async (item) => {
                try {
                    await requestAuthenticationApi(item.route, {
                        method: item.method,
                        body: item.body,
                        csrf: true,
                        idempotencyKey: item.idempotencyKey,
                        allowQueue: false
                    });
                    return { status: 200 };
                } catch (error) {
                    return { status: error.status || 0, error: error.code || 'network_error' };
                }
            }
        });
    };
    window.addEventListener('online', () => { void flushQueuedActions(); });

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
        renderMessagePlayerRecipients();
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

    const presenceController = createPresenceController({
        canPoll: () => authenticatedAccount?.role === 'dm'
            && activeView === 'dashboard'
            && !document.hidden
            && navigator.onLine,
        refresh: loadPresence,
        setInterval: window.setInterval.bind(window),
        clearInterval: window.clearInterval.bind(window)
    });

    const updatePresencePolling = () => {
        presenceController.stop();
        presenceRequestId++;
        authenticatedPresenceSnapshot = null;
        renderPresenceUi();
        presenceController.start();
    };

    const failClosedBeforeAuthenticationRestore = () => {
        beginAuthenticationGeneration();
        accountSessionController.setAccount(null);
        authenticationCsrfToken = '';
        clearProtectedFreshness();
        authenticatedXpSnapshot = null;
        authenticatedXpAwardsSnapshot = null;
        authenticatedWordCountSnapshot = null;
        authenticatedPresenceSnapshot = null;
        authenticatedQuestSnapshot = null;
        authenticatedMessageSnapshot = null;
        authenticatedRevisionSnapshot = null;
        messageLoading = false;
        messageError = '';
        document.querySelectorAll('[data-protected-content]').forEach((element) => { element.replaceChildren(); });
        updateAuthenticationUi();
        const requestedView = location.hash.slice(1) || 'dashboard';
        if (!protectedNavViews.has(requestedView)) setView(requestedView, false);
    };

    const restoreAuthentication = async () => {
        const restoreGeneration = authenticationGeneration;
        try {
            const session = await requestAuthenticationApi('/session');
            if (restoreGeneration !== authenticationGeneration) return;
            accountSessionController.setAccount(session.authenticated ? session.account : null);
            authenticatedResourceGeneration = session.authenticated
                && typeof session.resource_generation === 'string'
                ? session.resource_generation : '';
            authenticatedAbsoluteExpiresAt = session.authenticated
                ? Date.parse(String(session.absolute_expires_at || '')) : 0;
            authenticationCsrfToken = session.authenticated ? String(session.csrf_token || '') : '';
        } catch {
            if (restoreGeneration !== authenticationGeneration) return;
            messageDraftStore?.clear();
            messageDraftStore = null;
            document.querySelectorAll('#message-dm-text, #message-player-text').forEach((input) => { input.value = ''; });
            authenticatedAccount = null;
            authenticationCsrfToken = '';
        }
        clearProtectedFreshness();
        resetMagicItemState();
        authenticatedXpSnapshot = null;
        authenticatedXpAwardsSnapshot = null;
        xpAwardsLoading = null;
        xpAwardsError = '';
        resetLevelUpNotificationState();
        xpAwardsRequestId++;
        authenticatedWordCountSnapshot = null;
        authenticatedPresenceSnapshot = null;
        authenticatedQuestSnapshot = null;
        authenticatedMessageSnapshot = null;
        authenticatedRevisionSnapshot = null;
        appliedMessageRevision = null;
        appliedQuestRevision = null;
        revisionRequestId++;
        messageRequestId++;
        messageLoading = false;
        messageError = '';
        questStateFilter = '';
        lastQuestAlertSignature = '';
        updateAuthenticationUi();
        // Authentication UI may have failed closed to the dashboard. Reapply
        // the URL-selected view only after the session has been validated;
        // setView also enforces role and authorization boundaries.
        setView(location.hash.slice(1) || 'dashboard', false);
        updateRevisionPolling();
        if (authenticatedAccount !== null) {
            await Promise.all([loadXpSummary(), loadWordCountSummary(), loadQuests(), loadMessages()]);
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
    byId('message-notification-close')?.addEventListener('click', () => {
        const messageDialog = byId('message-notification-dialog');
        if (messageDialog instanceof HTMLDialogElement) messageDialog.close();
    });
    byId('message-notification-button')?.addEventListener('click', () => {
        const messageDialog = byId('message-notification-dialog');
        if (!(messageDialog instanceof HTMLDialogElement)
            || (authenticatedMessageSnapshot?.messages.length || 0) === 0) {
            return;
        }
        renderMessageNotifications();
        messageDialog.showModal();
    });
    byId('messages-retry')?.addEventListener('click', () => {
        void messagesActivityController.refresh();
    });
    byId('messages-next')?.addEventListener('click', () => {
        const cursor = authenticatedMessageSnapshot?.next_cursor;
        if (typeof cursor === 'string') void loadMessages(cursor);
    });
    byId('activity-refresh')?.addEventListener('click', async () => {
        await Promise.all([messagesActivityController.refresh(), loadQuests(), loadRevisions()]);
        renderActivityUi();
    });
    document.addEventListener('visibilitychange', updateRevisionPolling);
    window.addEventListener('online', updateRevisionPolling);
    window.addEventListener('offline', updateRevisionPolling);
    window.addEventListener('pageshow', (event) => {
        // A BFCache document can outlive the server session. Never reveal its
        // protected snapshot while the current session is being revalidated.
        failClosedBeforeAuthenticationRestore();
        void restoreAuthentication();
        const restorePublicHashView = () => {
            const currentView = location.hash.slice(1);
            if (!currentView) return false;
            if (!protectedNavViews.has(currentView)) setView(currentView, false);
            return true;
        };
        const restoreTimer = window.setInterval(() => {
            if (restorePublicHashView()) window.clearInterval(restoreTimer);
        }, 50);
        window.setTimeout(() => window.clearInterval(restoreTimer), 2000);
    });
    authDialog?.addEventListener('close', () => {
        void renderAuthenticatedHeroToken();
        showPendingLevelUpNotifications();
    });
    byId('level-up-alert-close')?.addEventListener('click', () => {
        const dialog = byId('level-up-alert-dialog');
        if (dialog instanceof HTMLDialogElement) dialog.close();
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
            beginAuthenticationGeneration();
            accountSessionController.setAccount(session.account);
            authenticatedResourceGeneration = typeof session.resource_generation === 'string'
                ? session.resource_generation : '';
            authenticatedAbsoluteExpiresAt = Date.parse(String(session.absolute_expires_at || ''));
            authenticationCsrfToken = String(session.csrf_token || '');
            clearProtectedFreshness();
            resetMagicItemState();
            authenticatedXpSnapshot = null;
            authenticatedXpAwardsSnapshot = null;
            xpAwardsLoading = null;
            xpAwardsError = '';
            resetLevelUpNotificationState();
            xpAwardsRequestId++;
            authenticatedWordCountSnapshot = null;
            authenticatedQuestSnapshot = null;
            authenticatedMessageSnapshot = null;
            authenticatedRevisionSnapshot = null;
            appliedMessageRevision = null;
            appliedQuestRevision = null;
            revisionRequestId++;
            messageRequestId++;
            messageLoading = false;
            messageError = '';
            questStateFilter = '';
            lastQuestAlertSignature = '';
            try {
                const identity = await requestAuthenticationApi('/me');
                accountSessionController.setAccount(identity.account || authenticatedAccount);
                messageDraftStore = authenticatedAccount?.id
                    ? createMessageDraftStore(localStorage, authenticatedAccount.id)
                    : null;
            } catch {
                // The login response is already bound to the same server session.
            }
            authLoginForm.reset();
            setAuthenticationMessage('');
            setAuthenticationMessage('Character login succeeded.', false, true);
            updateAuthenticationUi();
            updateRevisionPolling();
            await Promise.all([loadXpSummary(), loadWordCountSummary(), loadQuests(), loadMessages()]);
            try {
                await claimLevelUpNotifications();
            } catch {
                // Login remains available when optional level-up notification delivery fails.
            }
        } catch (error) {
            messageDraftStore?.clear();
            messageDraftStore = null;
            document.querySelectorAll('#message-dm-text, #message-player-text').forEach((input) => { input.value = ''; });
            authenticatedAccount = null;
            authenticationCsrfToken = '';
            clearProtectedFreshness();
            resetMagicItemState();
            authenticatedXpSnapshot = null;
            authenticatedXpAwardsSnapshot = null;
            xpAwardsLoading = null;
            xpAwardsError = '';
            resetLevelUpNotificationState();
            xpAwardsRequestId++;
            authenticatedWordCountSnapshot = null;
            authenticatedQuestSnapshot = null;
            authenticatedMessageSnapshot = null;
            authenticatedRevisionSnapshot = null;
            appliedMessageRevision = null;
            appliedQuestRevision = null;
            revisionRequestId++;
            messageRequestId++;
            messageLoading = false;
            messageError = '';
            questStateFilter = '';
            lastQuestAlertSignature = '';
            setAuthenticationMessage(error.message, true);
            updateAuthenticationUi();
            updateRevisionPolling();
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
            beginAuthenticationGeneration();
            messageDraftStore?.clear();
            messageDraftStore = null;
            document.querySelectorAll('#message-dm-text, #message-player-text').forEach((input) => { input.value = ''; });
            authenticatedAccount = null;
            authenticationCsrfToken = '';
            clearProtectedFreshness();
            resetMagicItemState();
            authenticatedXpSnapshot = null;
            xpRequestId++;
            authenticatedXpAwardsSnapshot = null;
            xpAwardsLoading = null;
            xpAwardsError = '';
            resetLevelUpNotificationState();
            xpAwardsRequestId++;
            authenticatedWordCountSnapshot = null;
            wordCountRequestId++;
            authenticatedQuestSnapshot = null;
            questRequestId++;
            authenticatedMessageSnapshot = null;
            messageRequestId++;
            authenticatedRevisionSnapshot = null;
            appliedMessageRevision = null;
            appliedQuestRevision = null;
            revisionRequestId++;
            messageLoading = false;
            messageError = '';
            questStateFilter = '';
            lastQuestAlertSignature = '';
            const questAlertDialog = byId('quest-alert-dialog');
            if (questAlertDialog instanceof HTMLDialogElement && questAlertDialog.open) {
                questAlertDialog.close();
            }
            const messageDialog = byId('message-notification-dialog');
            if (messageDialog instanceof HTMLDialogElement && messageDialog.open) {
                messageDialog.close();
            }
            const levelUpDialog = byId('level-up-alert-dialog');
            if (levelUpDialog instanceof HTMLDialogElement && levelUpDialog.open) {
                levelUpDialog.close();
            }
            updateAuthenticationUi();
            updateRevisionPolling();
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

    byId('xp-retry')?.addEventListener('click', () => {
        void loadXpSummary();
    });

    byId('quests-retry')?.addEventListener('click', () => {
        void loadQuests();
    });

    byId('xp-awards-retry')?.addEventListener('click', () => {
        void loadXpAwards(true);
    });

    byId('word-count-refresh')?.addEventListener('click', () => {
        void loadWordCountSummary();
    });

    updateAuthenticationUi();

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
        const onControllerChange = createControllerChangeHandler({
            getController: () => navigator.serviceWorker.controller,
            reload: () => window.location.reload()
        });
        navigator.serviceWorker.addEventListener('controllerchange', onControllerChange);
        let pendingServiceWorker = null;
        const updateBanner = byId('update-banner');
        const showServiceWorkerUpdate = (worker) => {
            pendingServiceWorker = worker;
            if (updateBanner) updateBanner.hidden = false;
        };
        byId('update-dismiss')?.addEventListener('click', () => {
            if (updateBanner) updateBanner.hidden = true;
        });
        const updateLifecycleController = createUpdateLifecycleController({
            apply: () => pendingServiceWorker?.postMessage({ type: 'SKIP_WAITING' })
        });
        byId('update-apply')?.addEventListener('click', () => {
            updateLifecycleController.requestApply();
        });

        window.addEventListener('load', async () => {
            try {
                const registration = await navigator.serviceWorker.register(
                    'service-worker.js',
                    { scope: './', updateViaCache: 'none' });
                if (registration.waiting) showServiceWorkerUpdate(registration.waiting);
                registration.addEventListener('updatefound', () => {
                    const worker = registration.installing;
                    if (!worker) return;
                    worker.addEventListener('statechange', () => {
                        if (worker.state === 'installed' && navigator.serviceWorker.controller) {
                            showServiceWorkerUpdate(worker);
                        }
                    });
                });
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

    const campaignSearch = initializeCampaignSearch({ byId });
    initializeTranslator({ byId });
    initializeDice({ byId });

    window.setTimeout(() => setView(location.hash.slice(1) || 'dashboard', false), 0);
    const initialHashRestoreTimer = window.setInterval(() => {
        const requestedView = location.hash.slice(1);
        if (!requestedView || protectedNavViews.has(requestedView) || !views.has(requestedView)) return;
        if (activeView !== requestedView) setView(requestedView, false);
        else window.clearInterval(initialHashRestoreTimer);
    }, 50);
    window.setTimeout(() => window.clearInterval(initialHashRestoreTimer), 30000);
    console.info(`${APP_NAME} ${APP_VERSION} initialized.`);
})();
