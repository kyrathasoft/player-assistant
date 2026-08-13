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
let xpAwardsProjected = false;
let messagesRead = false;

const readJsonBody = async (request) => {
    let body = '';
    for await (const chunk of request) body += chunk;
    return JSON.parse(body || '{}');
};

const requireSession = (request, response) => {
    if (hasSession(request)) return sessionAccount(request);
    jsonResponse(response, 401, { message: 'Authentication required.' }, expectedErrorResponse);
    return null;
};

const xpCharacter = (currentAccount) => ({
    character_name: currentAccount === secondPlayerAccount ? 'Maximilian' : currentAccount.character_name,
    character_class: 'Fighter',
    level_before_award: 1,
    xp_award: 0,
    xp_award_date: '8.07.2026',
    level_after_award: 1,
    level: 1,
    hit_points: 10,
    xp_total: currentAccount === playerAccount ? 2000 : 1200,
    xp_to_next_level: 3000
});

const xpAwardEntry = (currentAccount) => ({
    character_name: currentAccount.character_name,
    character_class: 'Fighter',
    level_before_award: 1,
    xp_award: 500,
    xp_award_date: '8.07.2026',
    level_after_award: 1
});

const xpProgression = (currentAccount) => ({
    character_key: currentAccount.character_key,
    is_account_character: true,
    entries: [
        {
            ...xpAwardEntry(currentAccount),
            xp_award: 400,
            xp_award_date: '7.31.2026'
        },
        ...(currentAccount === playerAccount && xpAwardsProjected ? [xpAwardEntry(currentAccount)] : [])
    ]
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
    xp_to_next_level: 3000
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
        request_status: null,
        wiki_url: 'https://publish.obsidian.md/scarlethorizons/Quests/CI+Quest'
    }],
    pending_requests: [],
    notifications: []
});

const messagePayload = (currentAccount) => ({
    schema_version: 2,
    messages: currentAccount === playerAccount && !messagesRead ? [{
        id: '11111111111111111111111111111111',
        sender_character_name: 'CI Dungeon Master',
        recipient_character_name: currentAccount.character_name,
        message: 'Browser smoke message',
        sent_at: '2026-08-07T12:00:00Z',
        read_at: null
    }] : [],
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
            ? { authenticated: true, account: sessionAccount(request), csrf_token: 'ci-csrf-token' }
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
        jsonResponse(response, 200, { account: selectedAccount, csrf_token: 'ci-csrf-token' }, {
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
                        xp_to_next_level: 5230
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
                        : [])
                ]
            });
        return;
    }
    if (route === '/xp-awards' && request.method === 'GET') {
        const currentAccount = requireSession(request, response);
        if (!currentAccount) return;
        if (currentAccount === playerAccount) xpAwardsProjected = true;
        const accounts = currentAccount.role === 'dm' ? [playerAccount, secondPlayerAccount] : [currentAccount];
        jsonResponse(response, 200, {
            schema_version: 1,
            scope: currentAccount.role === 'dm' ? 'party' : 'character',
            progressions: [
                ...accounts.map(xpProgression),
                ...(currentAccount === playerAccount ? [playerHirelingProgression] : [])
            ]
        });
        return;
    }
    if (route === '/quests' && request.method === 'GET') {
        const currentAccount = requireSession(request, response);
        if (!currentAccount) return;
        jsonResponse(response, 200, questPayload(currentAccount));
        return;
    }
    if (route === '/messages' && request.method === 'GET') {
        const currentAccount = requireSession(request, response);
        if (!currentAccount) return;
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
    const page = await context.newPage();
    await page.emulateMedia({ reducedMotion: 'reduce' });
    const pageErrors = [];
    const consoleErrors = [];
    const requestFailures = [];
    const requestUrls = [];
    const unexpectedResponses = [];
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
    page.on('request', (request) => requestUrls.push(request.url()));
    page.on('requestfailed', (request) => {
        if (!offlineExpected) {
            requestFailures.push(`${request.method()} ${request.url()}: ${request.failure()?.errorText || 'failed'}`);
        }
    });
    page.on('response', (response) => {
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
            self.addEventListener('error', (event) => console.error(`service-worker-error:${event.message}`));
            self.addEventListener('unhandledrejection', (event) => console.error(`service-worker-rejection:${event.reason}`));
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
    await page.locator('#auth-submit').click();
    await page.locator('#auth-button-label').getByText('CI Hero', { exact: true }).waitFor();
    if (await page.locator('#auth-account-panel').isHidden()) {
        throw new Error('Authentication smoke failed: the signed-in account panel stayed hidden.');
    }
    await page.locator('#auth-dialog-close').click();
    await page.locator('#auth-dialog').waitFor({ state: 'hidden' });
    if (!await page.locator('#auth-button').evaluate((button) => document.activeElement === button)) {
        throw new Error('Dialog focus restoration failed after closing character login.');
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
    await page.locator('[data-view="quests"]').click();
    await page.locator('#quest-list').waitFor({ state: 'visible' });
    await page.locator('#quest-list .quest-card').first().waitFor({ state: 'visible' });
    if (!(await page.locator('#quest-list .quest-card').first().textContent()).includes("CI Hero's test quest")) {
        throw new Error('Quest dashboard did not render the authenticated player quest.');
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
    await page.locator('#message-notification-list .message-notification').first().getByText('Mark as read', { exact: true }).click();
    await page.locator('#message-notification-button').waitFor({ state: 'hidden' });

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
    await page.locator('#auth-submit').click();
    await page.locator('#auth-button-label').getByText('Max', { exact: true }).waitFor();
    await page.locator('#auth-dialog-close').click();
    await page.locator('#auth-dialog').waitFor({ state: 'hidden' });
    await page.waitForFunction(() => document.querySelector('#xp-total')?.textContent?.startsWith('1,200'));
    await page.locator('[data-view="xp-awards"]').click();
    await page.locator('#xp-awards-list').waitFor({ state: 'visible' });
    const secondPlayerAwardText = await page.locator('#xp-awards-list').textContent();
    if (!secondPlayerAwardText.includes('Maximilian - Progress: 28.6% of the way toward Fighter Level 2')
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
    const dungeonMasterPresenceStatus = await page.evaluate(async () =>
        (await fetch('/scarlethorizons/api/v1/presence')).status);
    if (dungeonMasterPresenceStatus !== 200) {
        throw new Error(`Dungeon Master presence API returned ${dungeonMasterPresenceStatus}.`);
    }
    await page.locator('#auth-dialog-close').click();
    await page.locator('[data-view="xp-awards"]').click();
    await page.locator('#xp-awards-list').waitFor({ state: 'visible' });
    const dungeonMasterProgressItems = await page.locator('#xp-awards-list .xp-award-progress-list li').allTextContents();
    if (dungeonMasterProgressItems.length !== 2
        || dungeonMasterProgressItems[0] !== 'CI Hero is 67.3% of the way toward Fighter Level 5'
        || !dungeonMasterProgressItems[1].startsWith('Maximilian is ')
        || await page.locator('#xp-awards-list .xp-award-character .xp-award-progress-summary').count() !== 0) {
        throw new Error(`Dungeon Master XP Awards progress list was incorrect: ${JSON.stringify(dungeonMasterProgressItems)}`);
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
    if (![...workerUrls].some((url) => url.includes('/campaign-search-worker.js?v=78'))) {
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

    const cachedUrls = await page.evaluate(async () => {
        const urls = [];
        for (const cacheName of await caches.keys()) {
            const cache = await caches.open(cacheName);
            urls.push(...(await cache.keys()).map((request) => request.url));
        }
        return urls;
    });
    for (const requiredPath of [
        '/data/orcish.json',
        '/data/elvish.json',
        '/campaign-search.json'
    ]) {
        if (!cachedUrls.some((url) => new URL(url).pathname.endsWith(requiredPath))) {
            throw new Error(`Offline feature data was not cached: ${requiredPath}`);
        }
    }
    if (!cachedUrls.some((url) => url.endsWith('/campaign-search-worker.js?v=78'))) {
        throw new Error('Campaign search worker was not present in the offline shell cache.');
    }
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
