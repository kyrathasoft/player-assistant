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

const hasSession = (request) => /(?:^|;\s*)ci-session=authenticated(?:;|$)/u.test(request.headers.cookie || '');

const serveApi = async (request, response, pathname) => {
    const route = pathname.slice(apiPrefix.length) || '/';
    if (route === '/session' && request.method === 'GET') {
        const authenticated = hasSession(request);
        jsonResponse(response, 200, authenticated
            ? { authenticated: true, account, csrf_token: 'ci-csrf-token' }
            : { authenticated: false, account: null, csrf_token: '' });
        return;
    }
    if (route === '/login' && request.method === 'POST') {
        let body = '';
        for await (const chunk of request) body += chunk;
        const credentials = JSON.parse(body || '{}');
        if (credentials.character_name !== 'CI Hero' || credentials.password !== 'ci-password') {
            jsonResponse(response, 401, { message: 'Invalid CI credentials.' });
            return;
        }
        jsonResponse(response, 200, { account, csrf_token: 'ci-csrf-token' }, {
            'Set-Cookie': 'ci-session=authenticated; HttpOnly; SameSite=Strict; Path=/scarlethorizons/'
        });
        return;
    }
    if (route === '/me' && request.method === 'GET' && hasSession(request)) {
        jsonResponse(response, 200, { account });
        return;
    }
    jsonResponse(response, hasSession(request) ? 503 : 401, {
        message: 'The deterministic browser fixture does not provide this optional dashboard service.'
    });
};

const serveStatic = async (request, response, pathname) => {
    let relativePath = pathname.slice(pwaPrefix.length);
    if (relativePath === '' || relativePath.endsWith('/')) relativePath += 'index.html';
    relativePath = decodeURIComponent(relativePath).replaceAll('/', '\\');
    const filePath = normalize(join(pwaRoot, relativePath));
    if (relative(pwaRoot, filePath).startsWith('..')) {
        response.writeHead(404);
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
        response.writeHead(404, { 'Content-Type': 'text/plain; charset=utf-8' });
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
    page.on('pageerror', (error) => pageErrors.push(error));

    await page.goto(`${origin}${pwaPrefix}`, { waitUntil: 'domcontentloaded' });
    await page.locator('#dashboard-title').waitFor({ state: 'visible' });

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

    await page.waitForFunction(() => navigator.serviceWorker?.controller !== null);
    await context.setOffline(true);
    await page.reload({ waitUntil: 'domcontentloaded' });
    await page.locator('#view-translator').waitFor({ state: 'visible' });
    await page.locator('#translator-input').waitFor({ state: 'visible' });
    if (!page.url().startsWith(`${origin}${pwaPrefix}`)) {
        throw new Error('Offline startup smoke loaded outside the PWA scope.');
    }
    await context.setOffline(false);

    if (pageErrors.length > 0) {
        throw new Error(`PWA page error: ${pageErrors[0].stack || pageErrors[0].message}`);
    }

    console.log('PWA browser smoke passed: authentication, translation, search, dice, navigation, and offline startup.');
    await context.close();
} finally {
    if (browser) await browser.close();
    await new Promise((resolve, reject) => server.close((error) => error ? reject(error) : resolve()));
}
