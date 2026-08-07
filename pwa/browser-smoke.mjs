import { chromium } from 'playwright';
import { createServer } from 'node:http';
import { readFile, stat } from 'node:fs/promises';
import { dirname, extname, join, normalize, relative } from 'node:path';
import { fileURLToPath } from 'node:url';

const pwaRoot = dirname(fileURLToPath(import.meta.url));
const pwaPrefix = '/scarlethorizons/pwa/';
const apiPrefix = '/scarlethorizons/api/v1';
const account = Object.freeze({
    id: 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa',
    character_name: 'CI Hero',
    character_key: 'ci-hero',
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

const sessionRole = (request) => request.headers.cookie?.match(/(?:^|;\s*)ci-session=(player|dm)(?:;|$)/u)?.[1] || '';
const hasSession = (request) => sessionRole(request) !== '';
const sessionAccount = (request) => sessionRole(request) === 'dm' ? dungeonMasterAccount : account;
const expectedErrorResponse = { 'X-CI-Expected-Error': 'true' };

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
        let body = '';
        for await (const chunk of request) body += chunk;
        const credentials = JSON.parse(body || '{}');
        const isPlayer = credentials.character_name === 'CI Hero' && credentials.password === 'ci-password';
        const isDungeonMaster = credentials.character_name === 'CI Dungeon Master' && credentials.password === 'ci-dm-password';
        if (!isPlayer && !isDungeonMaster) {
            jsonResponse(response, 401, { message: 'Invalid CI credentials.' }, expectedErrorResponse);
            return;
        }
        const selectedAccount = isDungeonMaster ? dungeonMasterAccount : account;
        jsonResponse(response, 200, { account: selectedAccount, csrf_token: 'ci-csrf-token' }, {
            'Set-Cookie': `ci-session=${isDungeonMaster ? 'dm' : 'player'}; HttpOnly; SameSite=Strict; Path=/scarlethorizons/`
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
    const pageErrors = [];
    const consoleErrors = [];
    const requestFailures = [];
    const unexpectedResponses = [];
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
    page.on('requestfailed', (request) => {
        if (!offlineExpected) {
            requestFailures.push(`${request.method()} ${request.url()}: ${request.failure()?.errorText || 'failed'}`);
        }
    });
    page.on('response', (response) => {
        if (response.status() >= 400 && response.headers()['x-ci-expected-error'] !== 'true') {
            unexpectedResponses.push(`${response.status()} ${response.url()}`);
        }
    });
    context.on('serviceworker', (worker) => {
        void worker.evaluate(() => {
            self.addEventListener('error', (event) => console.error(`service-worker-error:${event.message}`));
            self.addEventListener('unhandledrejection', (event) => console.error(`service-worker-rejection:${event.reason}`));
        });
    });

    await page.goto(`${origin}${pwaPrefix}`, { waitUntil: 'domcontentloaded' });
    await page.locator('#dashboard-title').waitFor({ state: 'visible' });
    await page.waitForFunction(() => navigator.serviceWorker?.controller !== null);

    const anonymousPresenceStatus = await page.evaluate(async () =>
        (await fetch('/scarlethorizons/api/v1/presence')).status);
    if (anonymousPresenceStatus !== 401) {
        throw new Error(`Anonymous protected API returned ${anonymousPresenceStatus}.`);
    }

    await page.locator('#auth-button').click();
    await page.locator('#auth-character-name').fill('CI Hero');
    await page.locator('#auth-password').fill('ci-password');
    await page.locator('#auth-submit').click();
    await page.locator('#auth-button-label').getByText('CI Hero', { exact: true }).waitFor();
    if (await page.locator('#auth-account-panel').isHidden()) {
        throw new Error('Authentication smoke failed: the signed-in account panel stayed hidden.');
    }
    await page.locator('#auth-dialog-close').click();
    await page.locator('#auth-dialog').waitFor({ state: 'hidden' });

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
    await page.locator('#auth-character-name').fill('CI Dungeon Master');
    await page.locator('#auth-password').fill('ci-dm-password');
    await page.locator('#auth-submit').click();
    await page.locator('#auth-button-label').getByText('CI Dungeon Master', { exact: true }).waitFor();
    await page.locator('#online-users-summary').waitFor({ state: 'visible' });
    const dungeonMasterPresenceStatus = await page.evaluate(async () =>
        (await fetch('/scarlethorizons/api/v1/presence')).status);
    if (dungeonMasterPresenceStatus !== 200) {
        throw new Error(`Dungeon Master presence API returned ${dungeonMasterPresenceStatus}.`);
    }
    await page.locator('#auth-dialog-close').click();

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

    await page.locator('#translator-input').fill('hello');
    await page.locator('#translator-output').waitFor({ state: 'visible' });
    await page.waitForFunction(() => document.querySelector('#translator-output')?.value === 'zug');

    await page.locator('[data-view="search"]').click();
    await page.locator('#campaign-search').fill('Kirkilston');
    await page.locator('#search-results .search-result').first().waitFor({ state: 'visible' });

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
        '/data/ghukliak.json',
        '/campaign-search.json'
    ]) {
        if (!cachedUrls.some((url) => new URL(url).pathname.endsWith(requiredPath))) {
            throw new Error(`Offline feature data was not cached: ${requiredPath}`);
        }
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
    await page.waitForFunction(() => (document.querySelector('#translator-output')?.value || '').length > 0);
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
