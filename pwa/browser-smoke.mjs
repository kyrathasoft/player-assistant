import { chromium } from 'playwright';
import { createServer } from 'node:http';
import { readFile, stat } from 'node:fs/promises';
import { dirname, extname, join, normalize, relative } from 'node:path';
import { fileURLToPath } from 'node:url';

const pwaRoot = dirname(fileURLToPath(import.meta.url));
const pwaPrefix = '/scarlethorizons/pwa/';
const apiPrefix = '/scarlethorizons/api/v1';
const playerAccount = Object.freeze({
    id: 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa',
    character_name: 'CI Hero',
    character_key: 'ci-hero',
    role: 'player'
});
const secondPlayerAccount = Object.freeze({
    id: 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb',
    character_name: 'Max',
    character_key: 'maximilian',
    role: 'player'
});
const dungeonMasterAccount = Object.freeze({
    id: 'dddddddddddddddddddddddddddddddd',
    character_name: 'CI Dungeon Master',
    character_key: 'ci-dungeon-master',
    role: 'dm'
});
const formerPlayerAccount = Object.freeze({
    id: 'cccccccccccccccccccccccccccccccc',
    character_name: 'CI Hero',
    character_key: 'urvan',
    role: 'player'
});
const contentTypes = new Map([
    ['.css', 'text/css; charset=utf-8'],
    ['.html', 'text/html; charset=utf-8'],
    ['.js', 'text/javascript; charset=utf-8'],
    ['.json', 'application/json; charset=utf-8'],
    ['.png', 'image/png'],
    ['.webmanifest', 'application/manifest+json; charset=utf-8'],
    ['.webp', 'image/webp']
]);

const jsonResponse = (response, status, payload, headers = {}) => {
    if (activeProtectedAccount !== null && status >= 200 && status < 300) {
        const issuedAt = new Date();
        const expiresAt = new Date(issuedAt.getTime() + 120000);
        payload = {
            ...payload,
            _protected_resource: {
                schema_version: 1,
                account_id: activeProtectedAccount.id,
                resource: '/v1/protected',
                generation: '1'.repeat(64),
                issued_at: issuedAt.toISOString(),
                expires_at: expiresAt.toISOString(),
                resource_revision: '1'.repeat(64),
                nonce: `${activeProtectedAccount.id.slice(0, 16)}${String(++activeProtectedNonceCounter).padStart(16, '0')}`
            }
        };
    }
    response.writeHead(status, {
        'Cache-Control': 'no-store',
        'Content-Type': 'application/json; charset=utf-8',
        ...headers
    });
    response.end(JSON.stringify(payload));
};

const sessionRole = (request) => request.headers.cookie?.match(
    /(?:^|;\s*)ci-session=(player-a|player-b|dm)(?:;|$)/u)?.[1] || '';
const hasSession = (request) => sessionRole(request) !== '';
const sessionAccount = (request) => ({
    'player-a': playerAccount,
    'player-b': secondPlayerAccount,
    dm: dungeonMasterAccount
}[sessionRole(request)] || null);
const expectedErrorResponse = { 'X-CI-Expected-Error': 'true' };
const magicItemFixture = Object.freeze({
    schema_version: 1,
    source: 'https://publish.obsidian.md/scarlethorizons/Magic+Items/Kirkilston+Crew+Magic+Items',
    items: [
        ['Public Relic', 'all'],
        ['Canonical Relic', 'ci-hero'],
        ['First Name Leak', 'ci'],
        ['Substring Leak', 'hero'],
        ['Same First Name Leak', 'ci-rival']
    ].map(([name, viewers]) => ({
        name,
        description: `${name} description.`,
        'date-acquired': '8.17.2026',
        'meta-date-acquired': '8.17.2026',
        longevity: 'permanent',
        provenance: 'Browser smoke fixture',
        whereabouts: 'CI vault',
        'viewable-by': viewers
    }))
});
let activeProtectedAccount = null;
let activeProtectedGeneration = 1;
let activeProtectedNonceCounter = 0;
let xpAwardsProjected = false;
const levelUpNotificationClaims = new Map();
const levelUpNotificationAcknowledgements = new Map();
let levelUpClaimFailuresRemaining = 0;
let levelUpAcknowledgementAttempts = 0;
let levelUpAcknowledgementResponseLossesRemaining = 1;
let messagesRead = false;
let messageContinuationRequests = 0;
let revisionRequests = 0;
let messageRevisionGeneration = 0;
let messageListFailuresRemaining = 0;
let messageListRequests = 0;
let expireSessionOnNextRevision = false;
let presenceRequests = 0;
let delayMagicItemsForPlayerA = false;

const readJsonBody = async (request) => {
    let body = '';
    for await (const chunk of request) body += chunk;
    return JSON.parse(body || '{}');
};

const requireSession = (request, response) => {
    if (hasSession(request)) {
        activeProtectedAccount = sessionAccount(request);
        return activeProtectedAccount;
    }
    jsonResponse(response, 401, { message: 'Authentication required.' }, expectedErrorResponse);
    return null;
};

const xpCharacter = (currentAccount) => ({
    character_key: currentAccount.character_key,
    character_name: currentAccount === secondPlayerAccount ? 'Maximilian' : currentAccount.character_name,
    character_class: 'Fighter',
    level_before_award: 1,
    xp_award: 0,
    xp_award_date: '8.07.2026',
    level_after_award: 1,
    level: 1,
    hit_points: 10,
    xp_total: currentAccount === playerAccount ? 2000 : 1200,
    xp_to_next_level: 3000,
    level_up_target_level: 2,
    level_up_target_xp: 1500,
    level_up_attained: currentAccount === playerAccount
});

const xpAwardEntry = (currentAccount) => ({
    character_name: currentAccount.character_name,
    character_class: 'Fighter',
    level_before_award: 1,
    xp_award: 500,
    xp_award_date: '8.07.2026',
    level_after_award: 1
});

const xpProgression = (currentAccount, progressionKey = currentAccount.character_key) => ({
    character_key: progressionKey,
    is_account_character: true,
    entries: [
        {
            ...xpAwardEntry(currentAccount),
            xp_award: 400,
            xp_award_date: '7.31.2026'
        },
        ...(currentAccount === playerAccount && xpAwardsProjected ? [xpAwardEntry(currentAccount)] : [])
    ],
});

const playerHirelingProgression = Object.freeze({
    character_key: 'ci-hireling',
    is_account_character: false,
    entries: [{
        character_name: 'CI Hireling',
        character_class: 'Fighter',
        level_before_award: 1,
        xp_award: 250,
        xp_award_date: '8.01.2026',
        level_after_award: 1
    }]
});

const playerHirelingXp = Object.freeze({
    character_name: 'CI Hireling',
    character_class: 'Fighter',
    level_before_award: 1,
    xp_award: 0,
    xp_award_date: '8.07.2026',
    level_after_award: 1,
    level: 1,
    hit_points: 8,
    xp_total: 1000,
    xp_to_next_level: 3000,
    level_up_target_level: 2,
    level_up_target_xp: 4000,
    level_up_attained: false
});

const secondPlayerHirelingProgression = Object.freeze({
    character_key: 'corba-xp',
    is_account_character: false,
    entries: [{
        character_name: 'Corba',
        character_class: 'Ranger',
        level_before_award: 1,
        xp_award: 554,
        xp_award_date: '8.01.2026',
        level_after_award: 1
    }]
});

const secondPlayerHirelingXp = Object.freeze({
    character_name: 'Corba',
    character_class: 'Ranger',
    level_before_award: 1,
    xp_award: 0,
    xp_award_date: '8.07.2026',
    level_after_award: 1,
    level: 1,
    hit_points: 8,
    xp_total: 554,
    xp_to_next_level: 1696,
    level_up_target_level: 2,
    level_up_target_xp: 2250,
    level_up_attained: false
});

const questPayload = (currentAccount) => ({
    schema_version: 2,
    status_values: [
        'individual-only', 'party-only', 'individual-or-party',
        'gated', 'available', 'active', 'available (abandoned)', 'completed', 'withdrawn'
    ],
    request_status_values: ['pending', 'approved', 'denied'],
    quests: [{
        id: 'ci-quest',
        title: `${currentAccount.character_name}'s test quest`,
        summary: 'A deterministic browser-smoke quest.',
        quest_giver: 'CI Quest Giver',
        visibility: 'individual-or-party',
        state: 'available',
        objectives: ['Complete the browser-smoke objective.'],
        reward: 'Confidence',
        accepted_on: '',
        expires_on: '',
        completed_on: '',
        completed_meta_date: '',
        request_status: null,
        wiki_url: 'https://publish.obsidian.md/scarlethorizons/Quests/CI+Quest'
    }],
    pending_requests: [],
    notifications: []
});

const messagePayload = (currentAccount) => ({
    schema_version: 3,
    messages: currentAccount === playerAccount && !messagesRead ? [{
        id: '11111111111111111111111111111111',
        sender_character_name: 'CI Dungeon Master',
        recipient_character_name: currentAccount.character_name,
        message: 'Browser smoke message',
        sent_at: '2026-08-07T12:00:00Z',
        read_at: null
    }] : [],
    unread_count: currentAccount === playerAccount && !messagesRead ? 2 : 0,
    next_cursor: currentAccount === playerAccount && !messagesRead ? 'browser-smoke-cursor' : null,
    player_recipients: currentAccount === playerAccount ? [{
        account_id: secondPlayerAccount.id,
        character_name: secondPlayerAccount.character_name
    }] : currentAccount === secondPlayerAccount ? [{
        account_id: playerAccount.id,
        character_name: playerAccount.character_name
    }] : []
});

const serveApi = async (request, response, pathname) => {
    const route = pathname.slice(apiPrefix.length) || '/';
    if (route === '/session' && request.method === 'GET') {
        const authenticated = hasSession(request);
        jsonResponse(response, 200, authenticated
            ? { authenticated: true, account: sessionAccount(request), csrf_token: 'ci-csrf-token', resource_generation: '1'.repeat(64) }
            : { authenticated: false, account: null, csrf_token: '' });
        return;
    }
    if (route === '/login' && request.method === 'POST') {
        const credentials = await readJsonBody(request);
        const isPlayer = credentials.character_name === 'CI Hero' && credentials.password === 'ci-password';
        const isSecondPlayer = credentials.character_name === 'Max'
            && credentials.password === 'ci-second-password';
        const isDungeonMaster = credentials.character_name === 'CI Dungeon Master'
            && credentials.password === 'ci-dm-password';
        if (!isPlayer && !isSecondPlayer && !isDungeonMaster) {
            jsonResponse(response, 401, { message: 'Invalid CI credentials.' }, expectedErrorResponse);
            return;
        }
        const selectedAccount = isDungeonMaster
            ? dungeonMasterAccount
            : isSecondPlayer ? secondPlayerAccount : playerAccount;
        const session = isDungeonMaster ? 'dm' : isSecondPlayer ? 'player-b' : 'player-a';
        jsonResponse(response, 200, { account: selectedAccount, csrf_token: 'ci-csrf-token', resource_generation: '1'.repeat(64) }, {
            'Set-Cookie': `ci-session=${session}; HttpOnly; SameSite=Strict; Path=/scarlethorizons/`
        });
        return;
    }
    if (route === '/me' && request.method === 'GET' && hasSession(request)) {
        jsonResponse(response, 200, { account: sessionAccount(request) });
        return;
    }
    if (route === '/logout' && request.method === 'POST') {
        if (!hasSession(request)) {
            jsonResponse(response, 401, { message: 'Authentication required.' }, expectedErrorResponse);
            return;
        }
        if (request.headers['x-csrf-token'] !== 'ci-csrf-token') {
            jsonResponse(response, 403, { message: 'CSRF validation failed.' }, expectedErrorResponse);
            return;
        }
        jsonResponse(response, 200, { status: 'ok' }, {
            'Set-Cookie': 'ci-session=; Max-Age=0; HttpOnly; SameSite=Strict; Path=/scarlethorizons/'
        });
        return;
    }
    if (route === '/presence' && request.method === 'GET') {
        presenceRequests++;
        if (!hasSession(request)) {
            jsonResponse(response, 401, { message: 'Authentication required.' }, expectedErrorResponse);
            return;
        }
        if (sessionRole(request) !== 'dm') {
            jsonResponse(response, 403, { message: 'Dungeon Master access required.' }, expectedErrorResponse);
            return;
        }
        jsonResponse(response, 200, {
            schema_version: 2,
            scope: 'party',
            observed_at: new Date().toISOString(),
            active_window_seconds: 120,
            users: []
        });
        return;
    }
    if (route === '/xp' && request.method === 'GET') {
        const currentAccount = requireSession(request, response);
        if (!currentAccount) return;
        jsonResponse(response, 200, sessionRole(request) === 'dm'
            ? {
                schema_version: 1,
                date_label: 'As of 8.07.2026',
                stale: false,
                scope: 'party',
                characters: [
                    {
                        ...xpCharacter(playerAccount),
                        level: 4,
                        xp_total: 10770,
                        xp_to_next_level: 5230,
                        level_up_target_level: 5,
                        level_up_target_xp: 16000,
                        level_up_attained: false
                    },
                    xpCharacter(secondPlayerAccount)
                ]
            }
            : {
                schema_version: 1,
                date_label: 'As of 8.07.2026',
                stale: false,
                scope: 'character',
                character: xpCharacter(currentAccount),
                authorized_characters: [
                    { character_key: currentAccount.character_key, character: xpCharacter(currentAccount) },
                    ...(currentAccount === playerAccount
                        ? [{ character_key: 'ci-hireling', character: playerHirelingXp }]
                        : currentAccount === secondPlayerAccount
                            ? [{ character_key: 'corba-xp', character: secondPlayerHirelingXp }]
                            : [])
                ]
            });
        return;
    }
    if (route === '/xp-awards' && request.method === 'GET') {
        const currentAccount = requireSession(request, response);
        if (!currentAccount) return;
        if (currentAccount === playerAccount) xpAwardsProjected = true;
        const accounts = currentAccount.role === 'dm'
            ? [formerPlayerAccount, playerAccount, secondPlayerAccount]
            : [currentAccount];
        jsonResponse(response, 200, {
            schema_version: 1,
            scope: currentAccount.role === 'dm' ? 'party' : 'character',
            progressions: [
                ...accounts.map((account) => xpProgression(
                    account,
                    account === formerPlayerAccount ? 'urvan-xp' : account.character_key)),
                ...(currentAccount === playerAccount ? [playerHirelingProgression] : []),
                ...(currentAccount === secondPlayerAccount ? [secondPlayerHirelingProgression] : [])
            ]
        });
        return;
    }
    if (route === '/xp-level-up-notifications/claim' && request.method === 'POST') {
        const currentAccount = requireSession(request, response);
        if (!currentAccount) return;
        if (levelUpClaimFailuresRemaining > 0) {
            levelUpClaimFailuresRemaining -= 1;
            jsonResponse(
                response,
                503,
                { error: 'level_up_notifications_unavailable' },
                expectedErrorResponse);
            return;
        }
        const previousClaims = levelUpNotificationClaims.get(currentAccount.id) || 0;
        levelUpNotificationClaims.set(currentAccount.id, previousClaims + 1);
        const acknowledged = levelUpNotificationAcknowledgements.get(currentAccount.id) === true;
        jsonResponse(response, 200, {
            schema_version: 1,
            notifications: currentAccount === playerAccount && !acknowledged
                ? [
                    {
                        character_key: 'ci-hero',
                        character_name: 'CI Hero',
                        character_class: 'Fighter',
                        target_level: 2
                    },
                    {
                        character_key: 'ci-hireling',
                        character_name: 'CI Hireling',
                        character_class: 'Fighter',
                        target_level: 2
                    }
                ]
                : []
        });
        return;
    }
    if (route === '/xp-level-up-notifications/acknowledge' && request.method === 'POST') {
        const currentAccount = requireSession(request, response);
        if (!currentAccount) return;
        levelUpAcknowledgementAttempts += 1;
        const body = await readJsonBody(request);
        if (currentAccount !== playerAccount
            || !Array.isArray(body.notifications)
            || body.notifications.length !== 2) {
            jsonResponse(response, 400, { error: 'invalid_level_up_acknowledgement' });
            return;
        }
        const wasAcknowledged = levelUpNotificationAcknowledgements.get(currentAccount.id) === true;
        levelUpNotificationAcknowledgements.set(currentAccount.id, true);
        if (levelUpAcknowledgementResponseLossesRemaining > 0) {
            levelUpAcknowledgementResponseLossesRemaining -= 1;
            response.destroy();
            return;
        }
        jsonResponse(response, 200, {
            schema_version: 1,
            acknowledged_count: wasAcknowledged ? 0 : body.notifications.length
        });
        return;
    }
    if (route === '/magic-items' && request.method === 'GET') {
        const currentAccount = requireSession(request, response);
        if (!currentAccount) return;
        if (currentAccount === playerAccount && delayMagicItemsForPlayerA) {
            await new Promise((resolve) => setTimeout(resolve, 150));
        }
        const authorizedItems = magicItemFixture.items
            .filter((item) => item['viewable-by'] === 'all'
                || item['viewable-by'] === currentAccount.character_key)
            .map((item) => ({ ...item, 'viewable-by': 'all' }));
        jsonResponse(response, 200, {
            ...magicItemFixture,
            source: 'broker',
            items: authorizedItems
        });
        return;
    }
    if (route === '/quests' && request.method === 'GET') {
        const currentAccount = requireSession(request, response);
        if (!currentAccount) return;
        jsonResponse(response, 200, questPayload(currentAccount));
        return;
    }
    if (route === '/revisions' && request.method === 'GET') {
        const currentAccount = requireSession(request, response);
        if (!currentAccount) return;
        if (expireSessionOnNextRevision) {
            expireSessionOnNextRevision = false;
            response.writeHead(401, {
                ...expectedErrorResponse,
                'Content-Type': 'text/html; charset=utf-8',
                'Set-Cookie': 'ci-session=; Max-Age=0; HttpOnly; SameSite=Strict; Path=/scarlethorizons/'
            });
            response.end('<html><body>Session expired.</body></html>');
            return;
        }
        revisionRequests += 1;
        const playerActivity = currentAccount === playerAccount && !messagesRead;
        jsonResponse(response, 200, {
            schema_version: 1,
            observed_at: new Date().toISOString(),
            messages: {
                revision: playerActivity
                    ? (messageRevisionGeneration === 0 ? '1' : '3').repeat(64)
                    : '0'.repeat(64),
                activity_count: playerActivity ? 2 : 0,
                unread_count: playerActivity ? 2 : 0
            },
            quests: {
                revision: playerActivity ? '2'.repeat(64) : '0'.repeat(64),
                activity_count: 0
            }
        });
        return;
    }
    if (route === '/messages' && request.method === 'GET') {
        const currentAccount = requireSession(request, response);
        if (!currentAccount) return;
        const cursor = new URL(request.url || '/', 'http://127.0.0.1').searchParams.get('cursor');
        if (cursor !== null) {
            messageContinuationRequests += 1;
            jsonResponse(response, 503, { message: 'Simulated continuation failure.' }, expectedErrorResponse);
            return;
        }
        messageListRequests += 1;
        if (messageListFailuresRemaining > 0) {
            messageListFailuresRemaining -= 1;
            jsonResponse(response, 503, { message: 'Simulated message refresh failure.' }, expectedErrorResponse);
            return;
        }
        jsonResponse(response, 200, messagePayload(currentAccount));
        return;
    }
    if (route.match(/^\/messages\/[a-f0-9]{32}\/read$/u) && request.method === 'POST') {
        const currentAccount = requireSession(request, response);
        if (!currentAccount) return;
        if (request.headers['x-csrf-token'] !== 'ci-csrf-token') {
            jsonResponse(response, 403, { message: 'CSRF validation failed.' }, expectedErrorResponse);
            return;
        }
        messagesRead = true;
        jsonResponse(response, 200, { status: 'ok' });
        return;
    }
    if (route === '/quest-requests' && request.method === 'POST') {
        const currentAccount = requireSession(request, response);
        if (!currentAccount) return;
        if (currentAccount.role !== 'player' || request.headers['x-csrf-token'] !== 'ci-csrf-token') {
            jsonResponse(response, 403, { message: 'Quest request is not authorized.' }, expectedErrorResponse);
            return;
        }
        await readJsonBody(request);
        jsonResponse(response, 201, { status: 'pending' });
        return;
    }
    jsonResponse(response, hasSession(request) ? 503 : 401, {
        message: 'The deterministic browser fixture does not provide this optional dashboard service.'
    }, expectedErrorResponse);
};

const serveStatic = async (request, response, pathname) => {
    let relativePath = pathname.slice(pwaPrefix.length);
    if (relativePath === '' || relativePath.endsWith('/')) relativePath += 'index.html';
    if (relativePath === 'magic-items.json') {
        jsonResponse(response, 200, magicItemFixture);
        return;
    }
    relativePath = decodeURIComponent(relativePath).replaceAll('/', '\\');
    const filePath = normalize(join(pwaRoot, relativePath));
    if (relative(pwaRoot, filePath).startsWith('..')) {
        response.writeHead(404, expectedErrorResponse);
        response.end();
        return;
    }
    try {
        const metadata = await stat(filePath);
        if (!metadata.isFile()) throw new Error('not a file');
        const content = await readFile(filePath);
        response.writeHead(200, {
            'Cache-Control': 'no-cache',
            'Content-Length': String(content.byteLength),
            'Content-Type': contentTypes.get(extname(filePath).toLowerCase()) || 'application/octet-stream'
        });
        response.end(content);
    } catch {
        response.writeHead(404, { 'Content-Type': 'text/plain; charset=utf-8', ...expectedErrorResponse });
        response.end('Not found');
    }
};

const server = createServer(async (request, response) => {
    try {
        const url = new URL(request.url || '/', 'http://127.0.0.1');
        if (url.pathname.startsWith(apiPrefix)) {
            await serveApi(request, response, url.pathname);
            return;
        }
        if (url.pathname.startsWith(pwaPrefix)) {
            await serveStatic(request, response, url.pathname);
            return;
        }
        response.writeHead(302, { Location: pwaPrefix });
        response.end();
    } catch (error) {
        response.writeHead(500, { 'Content-Type': 'text/plain; charset=utf-8' });
        response.end(String(error));
    }
});

await new Promise((resolve, reject) => {
    server.once('error', reject);
    server.listen(0, '127.0.0.1', resolve);
});
const address = server.address();
if (!address || typeof address === 'string') throw new Error('Unable to start the browser fixture server.');
const origin = `http://127.0.0.1:${address.port}`;

let browser;
try {
    browser = await chromium.launch({ headless: true });
    const context = await browser.newContext({ serviceWorkers: 'allow' });
    await context.route(
        /https:\/\/publish(?:-01)?\.obsidian\.md\/.*Magic(?:\+|%20)Items/u,
        (route) => route.fulfill({
            status: 404,
            headers: { 'X-CI-Expected-Error': 'true' },
            body: 'Not available in the deterministic browser fixture.'
        }));
    const page = await context.newPage();
    await page.emulateMedia({ reducedMotion: 'reduce' });
    const pageErrors = [];
    const consoleErrors = [];
    const requestFailures = [];
    const requestUrls = [];
    const apiRequestHeaders = [];
    const unexpectedResponses = [];
    let acknowledgementReplayResponse = null;
    let initialResponseBytes = 0;
    const workerUrls = new Set();
    let offlineExpected = false;
    page.on('pageerror', (error) => pageErrors.push(error));
    context.on('console', (message) => {
        const text = message.text();
        if (message.type() === 'error'
            && !text.startsWith('Failed to load resource: the server responded with a status of')
            && !(offlineExpected && text.includes('net::ERR_INTERNET_DISCONNECTED'))) {
            consoleErrors.push(text);
        }
    });
    page.on('request', (request) => {
        requestUrls.push(request.url());
        if (request.url().includes('/scarlethorizons/api/v1/')) {
            apiRequestHeaders.push({
                method: request.method(),
                url: request.url(),
                headers: request.headers()
            });
        }
    });
    page.on('requestfailed', (request) => {
        const errorText = request.failure()?.errorText || 'failed';
        const expectedMagicItemCancellation = request.url().includes('/scarlethorizons/api/v1/magic-items')
            && errorText === 'net::ERR_ABORTED';
        if (!offlineExpected && !expectedMagicItemCancellation) {
            requestFailures.push(`${request.method()} ${request.url()}: ${errorText}`);
        }
    });
    page.on('response', (response) => {
        if (response.status() === 200
            && response.url().endsWith('/v1/xp-level-up-notifications/acknowledge')) {
            acknowledgementReplayResponse = response;
        }
        if (response.status() === 200 && !offlineExpected) {
            const contentLength = Number.parseInt(response.headers()['content-length'] || '', 10);
            if (Number.isFinite(contentLength)) initialResponseBytes += contentLength;
        }
        if (response.status() >= 400 && response.headers()['x-ci-expected-error'] !== 'true') {
            unexpectedResponses.push(`${response.status()} ${response.url()}`);
        }
    });
    page.on('worker', (worker) => workerUrls.add(worker.url()));
    context.on('serviceworker', (worker) => {
        void worker.evaluate(() => {
            self.addEventListener('error', (event) => console.error('service-worker-error'));
            self.addEventListener('unhandledrejection', (event) => console.error('service-worker-rejection'));
        });
    });

    await page.goto(`${origin}${pwaPrefix}`, { waitUntil: 'domcontentloaded' });
    await page.locator('#dashboard-title').waitFor({ state: 'visible' });
    await page.waitForFunction(() => navigator.serviceWorker?.controller !== null);

    if (initialResponseBytes <= 0) {
        throw new Error('Initial shell response byte measurement was unavailable.');
    }
    const initialOptionalRequests = await page.evaluate(() => {
        const navigation = performance.getEntriesByType('navigation')[0];
        const cutoff = navigation?.domContentLoadedEventEnd ?? 0;
        return performance.getEntriesByType('resource')
            .filter((entry) => entry.startTime <= cutoff
                && /(?:orcish|elvish|ghukliak|campaign-search)\.json(?:[?#]|$)/u.test(entry.name))
            .map((entry) => entry.name);
    });
    if (initialOptionalRequests.length > 0) {
        throw new Error(`Optional packs were requested during shell installation: ${JSON.stringify(initialOptionalRequests)}.`);
    }

    const navigationTiming = await page.evaluate(() => {
        const navigation = performance.getEntriesByType('navigation')[0];
        return navigation ? navigation.domContentLoadedEventEnd - navigation.startTime : NaN;
    });
    if (!Number.isFinite(navigationTiming) || navigationTiming > 5000) {
        throw new Error(`PWA startup exceeded the 5-second smoke budget: ${navigationTiming}ms.`);
    }
    const accessibilityContract = await page.evaluate(() => {
        const controls = [...document.querySelectorAll('button, input, select, textarea, a[href]')];
        return controls.filter((element) => {
            if (element.hidden || element.closest('[hidden]')) return false;
            const label = element.getAttribute('aria-label')
                || element.getAttribute('title')
                || element.textContent?.trim()
                || document.querySelector(`label[for="${element.id}"]`)?.textContent?.trim();
            return !label;
        }).map((element) => `${element.tagName}#${element.id}`);
    });
    if (accessibilityContract.length > 0) {
        throw new Error(`Visible control(s) lack an accessible name: ${accessibilityContract.join(', ')}`);
    }
    const hiddenLoadingDisplay = await page.evaluate(() => {
        const indicator = document.querySelector('#translation-loading');
        return {
            hidden: indicator?.hidden === true,
            display: indicator ? getComputedStyle(indicator).display : '',
            reducedMotion: matchMedia('(prefers-reduced-motion: reduce)').matches
        };
    });
    if (!hiddenLoadingDisplay.hidden || hiddenLoadingDisplay.display !== 'none'
        || !hiddenLoadingDisplay.reducedMotion) {
        throw new Error('The hidden translation loading indicator or reduced-motion contract failed.');
    }
    const reducedMotionVisualContract = await page.evaluate(() => {
        const probe = document.querySelector('#auth-button');
        if (!(probe instanceof HTMLElement)) return null;
        const style = getComputedStyle(probe);
        const durationsAreReduced = (value) => value.split(',').every((duration) => {
            const trimmed = duration.trim();
            const numeric = Number.parseFloat(trimmed);
            if (!Number.isFinite(numeric)) return false;
            return trimmed.endsWith('ms') ? numeric <= 0.01 : numeric <= 0.00001;
        });
        return {
            transitionDuration: style.transitionDuration,
            animationDuration: style.animationDuration,
            valid: durationsAreReduced(style.transitionDuration)
                && durationsAreReduced(style.animationDuration)
        };
    });
    if (!reducedMotionVisualContract?.valid) {
        throw new Error(`The reduced-motion visual contract failed: ${JSON.stringify(reducedMotionVisualContract)}.`);
    }

    await page.keyboard.press('Tab');
    const visibleFocusContract = await page.evaluate(() => {
        const active = document.activeElement;
        if (!(active instanceof HTMLElement)) return null;
        const style = getComputedStyle(active);
        return {
            id: active.id,
            outlineStyle: style.outlineStyle,
            outlineWidth: style.outlineWidth
        };
    });
    if (!visibleFocusContract
        || visibleFocusContract.outlineStyle === 'none'
        || Number.parseFloat(visibleFocusContract.outlineWidth) < 1) {
        throw new Error(`Visible keyboard focus contract failed: ${JSON.stringify(visibleFocusContract)}.`);
    }

    const anonymousPresenceStatus = await page.evaluate(async () =>
        (await fetch('/scarlethorizons/api/v1/presence')).status);
    if (anonymousPresenceStatus !== 401) {
        throw new Error(`Anonymous protected API returned ${anonymousPresenceStatus}.`);
    }

    const presenceRequestsBeforePlayerLogin = presenceRequests;
    await page.locator('#auth-button').click();
    if (!await page.locator('#auth-dialog').evaluate((dialog) => dialog.open
        && dialog.contains(document.activeElement))) {
        throw new Error('Opening the login dialog did not move focus inside the dialog.');
    }
    for (let index = 0; index < 8; index++) {
        await page.keyboard.press(index % 2 === 0 ? 'Tab' : 'Shift+Tab');
        const focusStayedInDialog = await page.locator('#auth-dialog').evaluate((dialog) =>
            dialog.open && dialog.contains(document.activeElement));
        if (!focusStayedInDialog) {
            throw new Error('Dialog focus containment failed while cycling keyboard focus.');
        }
    }
    await page.locator('#auth-character-name').fill('CI Hero');
    await page.locator('#auth-password').fill('wrong-password');
    await page.locator('#auth-submit').click();
    await page.waitForFunction(() => {
        const text = document.querySelector('#auth-message')?.textContent || '';
        return text !== '' && text !== 'Signing in…';
    });
    const failedLoginMessage = await page.locator('#auth-message').textContent();
    if (!failedLoginMessage.includes('Invalid CI credentials.')) {
        throw new Error(`Failed-login smoke did not expose the expected error: ${failedLoginMessage}`);
    }
    await page.locator('#auth-character-name').fill('CI Hero');
    await page.locator('#auth-password').fill('ci-password');
    messageListFailuresRemaining = 2;
    const initialMessageRequestsBeforeLogin = messageListRequests;
    await Promise.all([
        page.waitForResponse((response) => response.url().includes('/v1/messages?limit=50')
            && response.status() === 503),
        page.locator('#auth-submit').click()
    ]);
    await page.locator('#auth-button-label').getByText('CI Hero', { exact: true }).waitFor();
    if (await page.locator('#auth-account-panel').isHidden()) {
        throw new Error('Authentication smoke failed: the signed-in account panel stayed hidden.');
    }
    if (presenceRequests !== presenceRequestsBeforePlayerLogin) {
        throw new Error(`Player login started DM presence polling: ${presenceRequests}.`);
    }
    const loginRequest = apiRequestHeaders.find((entry) => entry.method === 'POST'
        && entry.url.endsWith('/v1/login'));
    if (!loginRequest
        || !/^[0-9a-f-]{16,}$/u.test(loginRequest.headers['x-request-id'] || '')
        || loginRequest.headers['idempotency-key']) {
        throw new Error(`Authentication request reliability headers were incorrect: ${JSON.stringify(loginRequest)}.`);
    }
    await page.locator('#auth-dialog-close').click();
    await page.locator('#auth-dialog').waitFor({ state: 'hidden' });
    await page.locator('#level-up-alert-dialog').waitFor({ state: 'visible' });
    const firstLevelUpAlert = await page.locator('#level-up-alert-list').textContent();
    if (!firstLevelUpAlert.includes('CI Hero reached Fighter Level 2')
        || !firstLevelUpAlert.includes('CI Hireling reached Fighter Level 2')
        || firstLevelUpAlert.includes('Did not level-up')) {
        throw new Error(`The first-login level-up notification was incorrect: ${firstLevelUpAlert}`);
    }
    for (let attempt = 0; attempt < 2_000 && acknowledgementReplayResponse === null; attempt += 1) {
        await page.waitForTimeout(10);
    }
    if (acknowledgementReplayResponse === null) {
        throw new Error('The browser did not receive the idempotent acknowledgement replay response.');
    }
    const acknowledgementReplayPayload = await acknowledgementReplayResponse.json();
    if (levelUpNotificationAcknowledgements.get(playerAccount.id) !== true
        || levelUpAcknowledgementAttempts !== 2
        || acknowledgementReplayPayload.acknowledged_count !== 0) {
        throw new Error(`The acknowledgement replay was incorrect: attempts=${levelUpAcknowledgementAttempts}, payload=${JSON.stringify(acknowledgementReplayPayload)}.`);
    }
    await page.waitForTimeout(5_500);
    if (levelUpAcknowledgementAttempts !== 2) {
        throw new Error(`The browser rejected the idempotent acknowledgement replay and retried again: ${levelUpAcknowledgementAttempts} attempts.`);
    }
    await page.locator('#level-up-alert-close').click();
    if (!await page.locator('#auth-button').evaluate((button) => document.activeElement === button)) {
        throw new Error('Dialog focus restoration failed after closing character login.');
    }
    for (let attempt = 0; attempt < 100 && messageListRequests < initialMessageRequestsBeforeLogin + 2; attempt++) {
        await page.waitForTimeout(10);
    }
    if (messageListRequests < initialMessageRequestsBeforeLogin + 2) {
        throw new Error('The first revision observation did not attempt the failed initial message refresh.');
    }
    await page.evaluate(() => {
        Object.defineProperty(document, 'hidden', { configurable: true, value: true });
        document.dispatchEvent(new Event('visibilitychange'));
    });
    await Promise.all([
        page.waitForResponse((response) => response.url().endsWith('/v1/revisions')),
        page.waitForResponse((response) => response.url().includes('/v1/messages?limit=50')
            && response.status() === 200),
        page.evaluate(() => {
            Object.defineProperty(document, 'hidden', { configurable: true, value: false });
            document.dispatchEvent(new Event('visibilitychange'));
        })
    ]);
    if (messageListRequests < initialMessageRequestsBeforeLogin + 2) {
        throw new Error('A failed initial message load was not retried from the first revision token.');
    }

    await page.waitForFunction(() => {
        const total = document.querySelector('#xp-total')?.textContent || '';
        const status = document.querySelector('#xp-status')?.textContent || '';
        return total !== '' || (status !== '' && !status.includes('Loading'));
    });
    const playerXpTotal = await page.locator('#xp-total').textContent();
    if (!playerXpTotal.startsWith('2,000')) {
        throw new Error(`Current XP dashboard did not render the expected total: ${playerXpTotal}; status=${await page.locator('#xp-status').textContent()}`);
    }
    await page.locator('[data-view="magic-items"]').click();
    await page.locator('#magic-item-list').waitFor({ state: 'visible' });
    const visibleMagicItems = await page.locator('#magic-item-list .magic-item-card').allTextContents();
    if (visibleMagicItems.length !== 2
        || !visibleMagicItems.some((text) => text.includes('Public Relic'))
        || !visibleMagicItems.some((text) => text.includes('Canonical Relic'))
        || visibleMagicItems.some((text) => /First Name Leak|Substring Leak|Same First Name Leak/u.test(text))) {
        throw new Error(`Magic-item canonical viewer isolation failed: ${JSON.stringify(visibleMagicItems)}.`);
    }
    await page.locator('[data-view="quests"]').click();
    await page.locator('#quest-list').waitFor({ state: 'visible' });
    await page.locator('#quest-list .quest-card').first().waitFor({ state: 'visible' });
    if (!(await page.locator('#quest-list .quest-card').first().textContent()).includes("CI Hero's test quest")) {
        throw new Error('Quest dashboard did not render the authenticated player quest.');
    }
    await page.locator('[data-view="activity"]').click();
    await page.locator('#view-activity').waitFor({ state: 'visible' });
    await page.locator('#activity-list').getByText('Browser smoke message', { exact: true }).waitFor();
    await page.waitForFunction(() => document.querySelector('#activity-status')?.textContent?.includes('2 active inbox items'));
    if (revisionRequests < 1) {
        throw new Error('Activity/Inbox did not load the lightweight revisions response.');
    }
    const revisionsBeforeHidden = revisionRequests;
    await page.evaluate(() => {
        Object.defineProperty(document, 'hidden', { configurable: true, value: true });
        document.dispatchEvent(new Event('visibilitychange'));
    });
    await page.waitForTimeout(100);
    if (revisionRequests !== revisionsBeforeHidden) {
        throw new Error('Activity revisions polling ran while the page was hidden.');
    }
    await Promise.all([
        page.waitForResponse((response) => response.url().endsWith('/v1/revisions')),
        page.evaluate(() => {
            Object.defineProperty(document, 'hidden', { configurable: true, value: false });
            document.dispatchEvent(new Event('visibilitychange'));
        })
    ]);

    messageRevisionGeneration = 1;
    messageListFailuresRemaining = 1;
    const messageRequestsBeforeFailedRevisionRefresh = messageListRequests;
    await page.evaluate(() => {
        Object.defineProperty(document, 'hidden', { configurable: true, value: true });
        document.dispatchEvent(new Event('visibilitychange'));
    });
    await Promise.all([
        page.waitForResponse((response) => response.url().endsWith('/v1/revisions')),
        page.waitForResponse((response) => response.url().includes('/v1/messages?limit=50')
            && response.status() === 503),
        page.evaluate(() => {
            Object.defineProperty(document, 'hidden', { configurable: true, value: false });
            document.dispatchEvent(new Event('visibilitychange'));
        })
    ]);
    await page.evaluate(() => {
        Object.defineProperty(document, 'hidden', { configurable: true, value: true });
        document.dispatchEvent(new Event('visibilitychange'));
    });
    await Promise.all([
        page.waitForResponse((response) => response.url().endsWith('/v1/revisions')),
        page.waitForResponse((response) => response.url().includes('/v1/messages?limit=50')
            && response.status() === 200),
        page.evaluate(() => {
            Object.defineProperty(document, 'hidden', { configurable: true, value: false });
            document.dispatchEvent(new Event('visibilitychange'));
        })
    ]);
    if (messageListRequests !== messageRequestsBeforeFailedRevisionRefresh + 2) {
        throw new Error('A failed revision-triggered message refresh was not retried.');
    }

    expireSessionOnNextRevision = true;
    await page.evaluate(() => {
        Object.defineProperty(document, 'hidden', { configurable: true, value: true });
        document.dispatchEvent(new Event('visibilitychange'));
    });
    await Promise.all([
        page.waitForResponse((response) => response.url().endsWith('/v1/revisions')
            && response.status() === 401),
        page.evaluate(() => {
            Object.defineProperty(document, 'hidden', { configurable: true, value: false });
            document.dispatchEvent(new Event('visibilitychange'));
        })
    ]);
    await page.locator('#auth-button-label').getByText('Log in', { exact: true }).waitFor();
    if (!(await page.locator('[data-view="activity"]').isHidden())
        || !(await page.locator('#auth-account-panel').isHidden())) {
        throw new Error('An expired polling session left protected UI visible.');
    }
    await page.locator('#auth-button').click();
    await page.locator('#auth-character-name').fill('CI Hero');
    await page.locator('#auth-password').fill('ci-password');
    await Promise.all([
        page.waitForResponse((response) => response.url().endsWith('/v1/xp-level-up-notifications/claim')),
        page.locator('#auth-submit').click()
    ]);
    await page.locator('#auth-button-label').getByText('CI Hero', { exact: true }).waitFor();
    await page.locator('#auth-dialog-close').click();
    await page.waitForTimeout(50);
    if (!(await page.locator('#level-up-alert-dialog').isHidden())) {
        throw new Error('A claimed level-up notification was repeated on a later login.');
    }

    await page.locator('[data-view="party-funds"]').click();
    await page.locator('#party-funds-total').waitFor({ state: 'visible' });
    await page.waitForFunction(() => document.querySelector('#party-funds-total')?.textContent?.trim() !== '—');

    await page.locator('[data-view="xp-awards"]').click();
    await page.locator('#xp-awards-list').waitFor({ state: 'visible' });
    const firstAwardRows = page.locator('#xp-awards-list tbody tr');
    await firstAwardRows.nth(1).waitFor({ state: 'visible' });
    const firstAwardText = await firstAwardRows.nth(1).textContent();
    if (!firstAwardText.includes('8.07.2026') || !firstAwardText.includes('500')) {
        throw new Error(`XP award projection did not create the expected single new award: ${firstAwardText}`);
    }
    const protectedTableSemantics = await page.locator('#xp-awards-list table').first().evaluate((table) => ({
        headerLabels: [...table.querySelectorAll('th[scope="col"]')].map((cell) => cell.textContent?.trim()),
        rowCount: table.querySelectorAll('tbody tr').length
    }));
    if (protectedTableSemantics.headerLabels.join('|') !== 'Date|XP award|Level'
        || protectedTableSemantics.rowCount !== 2) {
        throw new Error(`Protected XP Awards table semantics failed: ${JSON.stringify(protectedTableSemantics)}.`);
    }
    const playerProgressPresentation = await page.locator('#xp-awards-list .xp-award-progress-summary').first().evaluate((summary) => {
        const heading = summary.closest('h2');
        if (!(heading instanceof HTMLElement)) return { insideName: false };
        const headingStyle = getComputedStyle(heading);
        const summaryStyle = getComputedStyle(summary);
        const headingRect = heading.getBoundingClientRect();
        const summaryRect = summary.getBoundingClientRect();
        return {
            insideName: true,
            text: summary.textContent,
            font: summaryStyle.font,
            headingFont: headingStyle.font,
            sameLine: Math.abs(summaryRect.top - headingRect.top) < 2
        };
    });
    if (!playerProgressPresentation.insideName
        || playerProgressPresentation.text !== ' - Progress: 40.0% of the way toward Fighter Level 2'
        || await page.locator('#xp-awards-list .xp-award-character h2').first().textContent() !== 'CI Hero - Progress: 40.0% of the way toward Fighter Level 2'
        || await page.locator('#xp-awards-list .xp-award-character h2').nth(1).textContent() !== 'CI Hireling - Progress: 25.0% of the way toward Fighter Level 2'
        || playerProgressPresentation.font !== playerProgressPresentation.headingFont
        || !playerProgressPresentation.sameLine
        || await page.locator('#xp-awards-list > .xp-award-progress-section').count() !== 0) {
        throw new Error(`Player XP Awards progress presentation was incorrect: ${JSON.stringify(playerProgressPresentation)}`);
    }
    const playerAwardCardText = await page.locator('#xp-awards-list').textContent();
    if (playerAwardCardText.includes('Leveled-up:') || playerAwardCardText.includes('Did not level-up:')) {
        throw new Error(`XP Awards repeated a login-only level-up notification: ${playerAwardCardText}.`);
    }

    await page.setViewportSize({ width: 320, height: 800 });
    const protectedMobileLayout = await page.evaluate(() => ({
        viewport: document.documentElement.clientWidth,
        content: document.documentElement.scrollWidth,
        tableViewport: document.querySelector('#xp-awards-list table')?.getBoundingClientRect().width || 0
    }));
    if (protectedMobileLayout.content > protectedMobileLayout.viewport + 1
        || protectedMobileLayout.tableViewport <= 0) {
        throw new Error(`Protected narrow mobile layout overflows horizontally: ${JSON.stringify(protectedMobileLayout)}.`);
    }
    await page.setViewportSize({ width: 1280, height: 720 });

    const secondAwardResponse = await page.evaluate(async () => {
        const response = await fetch('/scarlethorizons/api/v1/xp-awards', {
            credentials: 'same-origin', cache: 'no-store'
        });
        return response.json();
    });
    const secondAwardEntries = secondAwardResponse.progressions[0].entries;
    if (secondAwardEntries.length !== 2
        || secondAwardEntries.filter((entry) => entry.xp_award_date === '8.07.2026').length !== 1
        || secondAwardEntries.filter((entry) => entry.xp_award === 500).length !== 1) {
        throw new Error('A repeated XP-awards refresh duplicated or changed the projected award.');
    }

    await page.locator('#message-notification-button').waitFor({ state: 'visible' });
    await page.locator('#message-notification-button').click();
    await page.locator('#message-notification-dialog').waitFor({ state: 'visible' });
    await page.locator('#message-notification-list .message-notification').first().getByText('Browser smoke message', { exact: true }).waitFor();
    const [continuationResponse] = await Promise.all([
        page.waitForResponse((response) => response.url().includes('/v1/messages?limit=50&cursor=')),
        page.locator('#messages-next').click()
    ]);
    if (continuationResponse.status() !== 503) {
        throw new Error(`Message continuation fixture returned ${continuationResponse.status()}.`);
    }
    await page.locator('#message-notification-list .message-notification').first().getByText('Browser smoke message', { exact: true }).waitFor();
    if (messageContinuationRequests !== 1) {
        throw new Error(`Message continuation failure fixture received ${messageContinuationRequests} requests.`);
    }
    await page.locator('#message-notification-list .message-notification').first().getByText('Mark as read', { exact: true }).click();
    await page.locator('#message-notification-button').waitFor({ state: 'hidden' });
    const readMessageRequest = [...apiRequestHeaders].reverse().find((entry) => entry.method === 'POST'
        && entry.url.includes('/v1/messages/') && entry.url.endsWith('/read'));
    if (!readMessageRequest
        || !/^[0-9a-f-]{16,}$/u.test(readMessageRequest.headers['x-request-id'] || '')
        || !/^[0-9a-f-]{16,}$/u.test(readMessageRequest.headers['idempotency-key'] || '')) {
        throw new Error(`Mutation reliability headers were incorrect: ${JSON.stringify(readMessageRequest)}.`);
    }

    const playerPresenceStatus = await page.evaluate(async () =>
        (await fetch('/scarlethorizons/api/v1/presence')).status);
    if (playerPresenceStatus !== 403) {
        throw new Error(`Player DM-only API returned ${playerPresenceStatus}.`);
    }
    const csrfFailureStatus = await page.evaluate(async () =>
        (await fetch('/scarlethorizons/api/v1/logout', { method: 'POST' })).status);
    if (csrfFailureStatus !== 403) {
        throw new Error(`Missing-CSRF logout returned ${csrfFailureStatus}.`);
    }

    await page.locator('#auth-button').click();
    await page.locator('#auth-logout').click();
    await page.locator('#auth-button-label').getByText('Log in', { exact: true }).waitFor();
    if (!(await page.locator('#auth-account-panel').isHidden())) {
        throw new Error('Logout left the account panel visible.');
    }

    await page.locator('#auth-button').click();
    await page.locator('#auth-character-name').fill('Max');
    await page.locator('#auth-password').fill('ci-second-password');
    levelUpClaimFailuresRemaining = 1;
    await Promise.all([
        page.waitForResponse((response) => response.url().endsWith('/v1/xp-level-up-notifications/claim')
            && response.status() === 503),
        page.locator('#auth-submit').click()
    ]);
    await page.locator('#auth-button-label').getByText('Max', { exact: true }).waitFor();
    await page.locator('#auth-dialog-close').click();
    await page.locator('#auth-dialog').waitFor({ state: 'hidden' });
    await page.waitForFunction(() => document.querySelector('#xp-total')?.textContent?.startsWith('1,200'));
    await page.locator('[data-view="magic-items"]').click();
    await page.locator('#magic-item-list').waitFor({ state: 'visible' });
    const secondPlayerMagicItems = await page.locator('#magic-item-list .magic-item-card').allTextContents();
    if (secondPlayerMagicItems.length !== 1
        || !secondPlayerMagicItems.some((text) => text.includes('Public Relic'))
        || secondPlayerMagicItems.some((text) => /Canonical Relic|First Name Leak|Substring Leak|Same First Name Leak/u.test(text))) {
        throw new Error(`Magic-item account transition leaked the prior account snapshot: ${JSON.stringify(secondPlayerMagicItems)}.`);
    }

    delayMagicItemsForPlayerA = true;
    await page.locator('#auth-button').click();
    await page.locator('#auth-logout').click();
    await page.locator('#auth-button-label').getByText('Log in', { exact: true }).waitFor();
    await page.locator('#auth-button').click();
    await page.locator('#auth-character-name').fill('CI Hero');
    await page.locator('#auth-password').fill('ci-password');
    await page.locator('#auth-submit').click();
    await page.locator('#auth-button-label').getByText('CI Hero', { exact: true }).waitFor();
    await page.locator('#auth-dialog-close').click();
    await page.locator('#auth-dialog').waitFor({ state: 'hidden' });
    const delayedMagicRequest = page.waitForRequest((request) =>
        request.url().includes('/v1/magic-items'));
    await page.locator('[data-view="magic-items"]').click();
    await delayedMagicRequest;
    await page.locator('#auth-button').click();
    await page.locator('#auth-logout').click();
    await page.locator('#auth-button-label').getByText('Log in', { exact: true }).waitFor();
    await page.locator('#auth-button').click();
    await page.locator('#auth-character-name').fill('Max');
    await page.locator('#auth-password').fill('ci-second-password');
    await page.locator('#auth-submit').click();
    await page.locator('#auth-button-label').getByText('Max', { exact: true }).waitFor();
    await page.locator('#auth-dialog-close').click();
    await page.locator('#auth-dialog').waitFor({ state: 'hidden' });
    await page.locator('[data-view="magic-items"]').click();
    await page.locator('#magic-item-list').waitFor({ state: 'visible' });
    await page.waitForTimeout(250);
    const delayedMagicItems = await page.locator('#magic-item-list .magic-item-card').allTextContents();
    delayMagicItemsForPlayerA = false;
    if (delayedMagicItems.length !== 1
        || !delayedMagicItems.some((text) => text.includes('Public Relic'))
        || delayedMagicItems.some((text) => /Canonical Relic|First Name Leak|Substring Leak|Same First Name Leak/u.test(text))) {
        throw new Error(`Delayed prior-account magic items repopulated the current account: ${JSON.stringify(delayedMagicItems)}.`);
    }

    await page.locator('[data-view="xp-awards"]').click();
    await page.locator('#xp-awards-list').waitFor({ state: 'visible' });
    const secondPlayerAwardText = await page.locator('#xp-awards-list').textContent();
    if (!secondPlayerAwardText.includes('Maximilian - Progress: 28.6% of the way toward Fighter Level 2')
        || !secondPlayerAwardText.includes('Corba - Progress: 24.6% of the way toward Ranger Level 2')
        || secondPlayerAwardText.includes('Leveled-up:')
        || secondPlayerAwardText.includes('Did not level-up:')
        || secondPlayerAwardText.includes('Max Progress toward')
        || secondPlayerAwardText.includes('CI Hero')) {
        throw new Error('Account switching or cross-account XP filtering failed.');
    }
    const secondPlayerMessages = await page.evaluate(async () => {
        const response = await fetch('/scarlethorizons/api/v1/messages', {
            credentials: 'same-origin', cache: 'no-store'
        });
        return response.json();
    });
    if (secondPlayerMessages.messages.length !== 0
        || !secondPlayerMessages.player_recipients.some((recipient) => recipient.character_name === 'CI Hero')) {
        throw new Error('Cross-account message filtering failed.');
    }

    await page.locator('#auth-button').click();
    await page.locator('#auth-logout').click();
    await page.locator('#auth-button-label').getByText('Log in', { exact: true }).waitFor();

    const presenceRequestsBeforeDmLogin = presenceRequests;
    await page.locator('#auth-button').click();
    await page.locator('#auth-character-name').fill('CI Dungeon Master');
    await page.locator('#auth-password').fill('ci-dm-password');
    await page.locator('#auth-submit').click();
    await page.locator('#auth-button-label').getByText('CI Dungeon Master', { exact: true }).waitFor();
    await page.locator('#auth-dashboard-token').waitFor({ state: 'visible' });
    await page.waitForFunction(() => {
        const image = document.querySelector('#auth-dashboard-token');
        return image?.complete && image.naturalWidth > 0 && image.naturalHeight > 0;
    });
    const dungeonMasterToken = await page.locator('#auth-dashboard-token').evaluate((image) => ({
        src: image.getAttribute('src'),
        alt: image.getAttribute('alt'),
        hidden: image.hidden,
        naturalWidth: image.naturalWidth,
        naturalHeight: image.naturalHeight
    }));
    if (!new URL(dungeonMasterToken.src || '', page.url()).pathname.endsWith('/data/hero-tokens/dungeon-master.webp')
        || dungeonMasterToken.alt !== 'Dungeon Master token'
        || dungeonMasterToken.hidden
        || dungeonMasterToken.naturalWidth === 0
        || dungeonMasterToken.naturalHeight === 0) {
        throw new Error(`Dungeon Master token did not render correctly: ${JSON.stringify(dungeonMasterToken)}.`);
    }
    await page.locator('#online-users-summary').waitFor({ state: 'visible' });
    if (presenceRequests <= presenceRequestsBeforeDmLogin) {
        throw new Error('Dungeon Master dashboard did not load presence data.');
    }
    await page.evaluate(() => {
        Object.defineProperty(document, 'hidden', { configurable: true, value: true });
        document.dispatchEvent(new Event('visibilitychange'));
    });
    await page.waitForTimeout(100);
    const presenceRequestsAfterHiddenSettled = presenceRequests;
    await page.waitForTimeout(150);
    if (presenceRequests !== presenceRequestsAfterHiddenSettled) {
        throw new Error('Presence requests continued while the document was hidden.');
    }
    await page.evaluate(() => {
        Object.defineProperty(document, 'hidden', { configurable: true, value: false });
        document.dispatchEvent(new Event('visibilitychange'));
    });
    const dungeonMasterPresenceStatus = await page.evaluate(async () =>
        (await fetch('/scarlethorizons/api/v1/presence')).status);
    if (dungeonMasterPresenceStatus !== 200) {
        throw new Error(`Dungeon Master presence API returned ${dungeonMasterPresenceStatus}.`);
    }
    await page.locator('#auth-dialog-close').click();
    await page.locator('[data-view="xp-awards"]').click();
    await page.locator('#xp-awards-list').waitFor({ state: 'visible' });
    const dungeonMasterAwardHeadings = await page.locator('#xp-awards-list .xp-award-character h2').allTextContents();
    if (dungeonMasterAwardHeadings[0]?.trim() !== 'CI Hero'
        || dungeonMasterAwardHeadings[1]?.trim() !== 'CI Hero - 10,770 XP (TNL: 5,230)'
        || dungeonMasterAwardHeadings[2]?.trim() !== 'Max - 1,200 XP (TNL: 3,000)') {
        throw new Error(`Dungeon Master XP Awards headings did not preserve stable identity matching: ${JSON.stringify(dungeonMasterAwardHeadings)}`);
    }
    const dungeonMasterProgressItems = await page.locator('#xp-awards-list .xp-award-progress-list li').allTextContents();
    if (dungeonMasterProgressItems.length !== 2
        || dungeonMasterProgressItems[0] !== 'CI Hero is 67.3% of the way toward Fighter Level 5'
        || !dungeonMasterProgressItems[1].startsWith('Maximilian is ')
        || await page.locator('#xp-awards-list .xp-award-character .xp-award-progress-summary').count() !== 0) {
        throw new Error(`Dungeon Master XP Awards progress list was incorrect: ${JSON.stringify(dungeonMasterProgressItems)}`);
    }
    const dungeonMasterAwardCardText = await page.locator('#xp-awards-list').textContent();
    if (dungeonMasterAwardCardText.includes('Leveled-up:')
        || dungeonMasterAwardCardText.includes('Did not level-up:')) {
        throw new Error(`Dungeon Master XP Awards repeated a login-only level-up notification: ${dungeonMasterAwardCardText}.`);
    }

    await context.clearCookies();
    await page.reload({ waitUntil: 'domcontentloaded' });
    await page.locator('#auth-button-label').getByText('Log in', { exact: true }).waitFor();
    if (!(await page.locator('#auth-account-panel').isHidden())) {
        throw new Error('Expired session left the account panel visible.');
    }

    await page.locator('[data-view="translator"]').click();
    await page.locator('#view-translator').waitFor({ state: 'visible' });
    if (!page.url().endsWith('#translator')) {
        throw new Error(`Navigation smoke failed: unexpected URL ${page.url()}`);
    }

    await page.locator('#translator-language').selectOption('elvish');
    await page.waitForFunction(() => window.localStorage.getItem('player-assistant.translator-language') === 'elvish');
    await page.reload({ waitUntil: 'domcontentloaded' });
    await page.locator('#view-translator').waitFor({ state: 'visible' });
    if (await page.locator('#translator-language').inputValue() !== 'elvish') {
        throw new Error('Translator did not restore the last-used language.');
    }
    await page.waitForFunction(() => document.querySelector('#lexicon-status')?.textContent?.includes('Elvish lexicon ready'));
    await page.locator('#translator-language').selectOption('orcish');
    await page.waitForFunction(() => window.localStorage.getItem('player-assistant.translator-language') === 'orcish');

    await page.locator('#translator-input').fill('hello');
    await page.waitForFunction(() => document.querySelector('#translator-output')?.value === 'zug');
    await page.locator('#translator-remove-pack').click();
    await page.waitForFunction(() => document.querySelector('#lexicon-status')?.textContent?.includes('pack removed'));
    await page.locator('#translator-input').fill('hello');
    await page.waitForFunction(() => document.querySelector('#translator-output')?.value === 'zug');
    await page.waitForFunction(async () => new Promise((resolve) => {
        const request = indexedDB.open('player-assistant-lexicons', 1);
        request.onsuccess = () => {
            const transaction = request.result.transaction('compiled', 'readonly');
            const keysRequest = transaction.objectStore('compiled').getAllKeys();
            keysRequest.onsuccess = () => resolve(keysRequest.result.some((key) => String(key).startsWith('orcish:1:')));
            keysRequest.onerror = () => resolve(false);
        };
        request.onerror = () => resolve(false);
    }));

    await page.locator('[data-view="search"]').click();
    await page.locator('#campaign-search').fill('Kirkilston');
    await page.locator('#search-results .search-result').first().waitFor({ state: 'visible' });
    await page.locator('#campaign-search-remove-pack').click();
    await page.waitForFunction(() => document.querySelector('#search-guidance')?.textContent?.includes('pack removed'));
    await page.locator('#campaign-search-retry-pack').click();
    await page.waitForFunction(() => document.querySelector('#search-guidance')?.textContent?.includes('pack ready offline'));
    await page.locator('#campaign-search').fill('Kirkilston');
    await page.locator('#search-results .search-result').first().waitFor({ state: 'visible' });
    const pwaAppRevision = await page.evaluate(() => globalThis.PLAYER_ASSISTANT_VERSION_METADATA?.appRevision);
    if (![...workerUrls].some((url) => url.includes(`/campaign-search-worker.js?v=${pwaAppRevision}`))) {
        throw new Error(`Campaign search did not start its dedicated worker: ${JSON.stringify([...workerUrls])}.`);
    }

    await page.locator('[data-view="dice"]').click();
    await page.locator('[data-die="1d20"]').click();
    const diceTotal = Number.parseInt(await page.locator('#dice-result strong').textContent() || '', 10);
    if (!Number.isInteger(diceTotal) || diceTotal < 1 || diceTotal > 20) {
        throw new Error(`Dice smoke failed: unexpected d20 total ${diceTotal}.`);
    }
    await page.locator('#dice-history li').first().getByText('1d20', { exact: true }).waitFor();

    await page.locator('[data-view="translator"]').click();

    const cachedEntries = await page.evaluate(async () => {
        const entries = [];
        for (const cacheName of await caches.keys()) {
            const cache = await caches.open(cacheName);
            entries.push(...(await cache.keys()).map((request) => ({ cacheName, url: request.url })));
        }
        return entries;
    });
    const optionalEntries = cachedEntries.filter(({ cacheName }) => cacheName.startsWith('player-assistant-optional-pack-'));
    for (const requiredPack of ['translator-orcish', 'translator-elvish', 'campaign-search']) {
        if (!optionalEntries.some(({ cacheName, url }) => cacheName === `player-assistant-optional-pack-${requiredPack}`
            && url.includes('pack-hash=') && !url.endsWith('pack-hash='))) {
            throw new Error(`Validated optional pack was not content-addressed and cached: ${requiredPack}`);
        }
    }
    if (cachedEntries.some(({ cacheName, url }) => !cacheName.startsWith('player-assistant-optional-pack-')
        && /(?:orcish|elvish|ghukliak|campaign-search)\.json(?:[?#]|$)/u.test(url))) {
        throw new Error('Optional pack was stored in the install shell or general data cache.');
    }
    if (!cachedEntries.some(({ url }) => url.endsWith(`/campaign-search-worker.js?v=${pwaAppRevision}`))) {
        throw new Error('Campaign search worker was not present in the offline shell cache.');
    }
    await page.waitForFunction(async (revision) => {
        const requests = [];
        for (const cacheName of await caches.keys()) {
            const cache = await caches.open(cacheName);
            requests.push(...await cache.keys());
        }
        const requiredShellModules = ['/app.js', '/correlation.js'];
        return requiredShellModules.every((path) => requests.some((request) => new URL(request.url).pathname.endsWith(path)
            && new URL(request.url).search === `?v=${revision}`));
    }, pwaAppRevision);
    await page.evaluate(async () => {
        await fetch('/scarlethorizons/api/v1/session');
        await fetch('/scarlethorizons/pwa/protected-future-data.json');
    });
    const protectedCacheUrls = await page.evaluate(async () => {
        const urls = [];
        for (const cacheName of await caches.keys()) {
            const cache = await caches.open(cacheName);
            urls.push(...(await cache.keys()).map((request) => request.url));
        }
        return urls;
    });
    if (protectedCacheUrls.some((url) => new URL(url).pathname.includes('/scarlethorizons/api/'))) {
        throw new Error('Service-worker cache contained a broker API response.');
    }
    if (protectedCacheUrls.some((url) => new URL(url).pathname.endsWith('/protected-future-data.json'))) {
        throw new Error('Service-worker cache contained an unknown same-origin response.');
    }

    offlineExpected = true;
    await context.setOffline(true);
    await page.reload({ waitUntil: 'domcontentloaded' });
    await page.locator('#view-translator').waitFor({ state: 'visible' });
    await page.locator('#translator-input').waitFor({ state: 'visible' });
    if (!page.url().startsWith(`${origin}${pwaPrefix}`)) {
        throw new Error('Offline startup smoke loaded outside the PWA scope.');
    }

    await page.waitForFunction(() => document.querySelector('#lexicon-status')?.textContent?.includes('lexicon ready'));
    await page.locator('#translator-input').fill('');
    await page.locator('#translator-input').fill('hello');
    try {
        await page.waitForFunction(() => (document.querySelector('#translator-output')?.value || '').length > 0);
    } catch (error) {
        const diagnostics = await page.evaluate(() => ({
            output: document.querySelector('#translator-output')?.value || '',
            status: document.querySelector('#lexicon-status')?.textContent || '',
            caches: [...Object.keys(window)].filter((key) => key.includes('Worker'))
        }));
        throw new Error(`Offline translator smoke failed to produce output: ${JSON.stringify(diagnostics)}; ${error.message}`);
    }
    const offlineTranslation = await page.locator('#translator-output').inputValue();
    if (offlineTranslation !== 'zug') {
        throw new Error(`Offline translator smoke failed: ${offlineTranslation}`);
    }

    await page.locator('[data-view="search"]').click();
    await page.locator('#campaign-search').fill('Kirkilston');
    await page.locator('#search-results .search-result').first().waitFor({ state: 'visible' });

    await page.locator('[data-view="dice"]').click();
    const offlineDiceHistoryCount = await page.locator('#dice-history li').count();
    await page.locator('[data-die="1d20"]').click();
    await page.waitForFunction(
        (previousCount) => document.querySelectorAll('#dice-history li').length > previousCount,
        offlineDiceHistoryCount);
    const offlineDiceTotal = Number.parseInt(await page.locator('#dice-result strong').textContent() || '', 10);
    if (!Number.isInteger(offlineDiceTotal) || offlineDiceTotal < 1 || offlineDiceTotal > 20) {
        throw new Error(`Offline dice smoke failed: unexpected d20 total ${offlineDiceTotal}.`);
    }
    await page.locator('#dice-history li').first().getByText('1d20', { exact: true }).waitFor();
    await context.setOffline(false);
    offlineExpected = false;

    const mobilePage = await context.newPage();
    await mobilePage.setViewportSize({ width: 320, height: 800 });
    await mobilePage.emulateMedia({ reducedMotion: 'reduce' });
    await mobilePage.goto(`${origin}${pwaPrefix}`, { waitUntil: 'domcontentloaded' });
    await mobilePage.locator('#dashboard-title').waitFor({ state: 'visible' });
    const mobileLayout = await mobilePage.evaluate(() => ({
        viewport: document.documentElement.clientWidth,
        content: document.documentElement.scrollWidth
    }));
    if (mobileLayout.content > mobileLayout.viewport + 1) {
        throw new Error(`Narrow mobile layout overflows horizontally: ${JSON.stringify(mobileLayout)}.`);
    }
    await mobilePage.close();

    if (pageErrors.length > 0) {
        throw new Error(`PWA page error: ${pageErrors[0].stack || pageErrors[0].message}`);
    }
    if (consoleErrors.length > 0) {
        throw new Error(`PWA console error: ${consoleErrors[0]}`);
    }
    if (requestFailures.length > 0) {
        throw new Error(`Unexpected PWA request failure: ${requestFailures[0]}`);
    }
    if (unexpectedResponses.length > 0) {
        throw new Error(`Unexpected PWA HTTP response: ${unexpectedResponses[0]}`);
    }

    console.log('PWA browser smoke passed: diagnostics, player/DM authorization, logout/session expiry, navigation, and online/offline features.');
    await context.close();
} finally {
    if (browser) await browser.close();
    await new Promise((resolve, reject) => server.close((error) => error ? reject(error) : resolve()));
}
