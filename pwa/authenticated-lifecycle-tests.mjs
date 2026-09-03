import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const sourcePath = process.argv[2] || new URL('./app.js', import.meta.url);
const source = await readFile(sourcePath, 'utf8');
const requestStart = source.indexOf('const requestAuthenticationApi = async');
const requestEnd = source.indexOf('\n    const validatePresenceSnapshot', requestStart);
const request = source.slice(requestStart, requestEnd);
const bodyParse = request.indexOf('await response.json()');
const statusGate = request.indexOf('if (response.status === 401 && path !== \'/login\')');
const postDecodeGenerationCheck = request.indexOf(
    'if (requestGeneration !== authenticationGeneration && path !== \'/login\')',
    bodyParse);

assert.ok(statusGate >= 0, '401 responses must be handled before body parsing.');
assert.ok(statusGate < bodyParse, '401 cleanup must not await a response body.');
assert.match(request.slice(statusGate, bodyParse), /clearExpiredAuthentication\(controller\)/u);
assert.match(request.slice(statusGate, bodyParse), /requestGeneration === authenticationGeneration/u);
assert.ok(postDecodeGenerationCheck > bodyParse, 'Generation must be rechecked after decoding.');
assert.ok(request.lastIndexOf('finally {') > bodyParse, 'Abort cleanup must cover response decoding.');
assert.match(request, /window\.clearTimeout\(timeoutId\)[\s\S]*activeAuthenticationControllers\.delete\(controller\)/u);

const pollingStart = source.indexOf('const updateRevisionPolling = () =>');
const pollingEnd = source.indexOf('\n    const renderMessageNotifications', pollingStart);
const polling = source.slice(pollingStart, pollingEnd);
assert.match(polling, /clearInterval\(revisionPollTimer\)/u);
assert.match(polling, /void loadRevisions\(\)/u);
assert.match(polling, /revisionPollTimer = window\.setInterval/u);
assert.ok(polling.indexOf('void loadRevisions()') < polling.indexOf('window.setInterval'),
    'Resume must issue its immediate request before installing polling.');
assert.equal((polling.match(/window\.setInterval/g) || []).length, 1,
    'Revision lifecycle must install one polling interval.');

const pageshow = source.indexOf("window.addEventListener('pageshow'");
assert.ok(pageshow >= 0, 'Authentication must revalidate on pageshow.');
const pageshowHandler = source.slice(pageshow, source.indexOf("\n    authDialog?.addEventListener", pageshow));
assert.match(pageshowHandler, /failClosedBeforeAuthenticationRestore\(\)/u);
assert.match(pageshowHandler, /void restoreAuthentication\(\)/u);
assert.match(source, /const failClosedBeforeAuthenticationRestore = \(\) =>/u);
assert.match(source, /accountSessionController\.setAccount\(null\)/u);
assert.match(source, /data-protected-content/u);
const failClosedStart = source.indexOf('const failClosedBeforeAuthenticationRestore = () =>');
const clearProtectedDom = source.indexOf("document.querySelectorAll('[data-protected-content]')", failClosedStart);
const failClosedUi = source.indexOf('updateAuthenticationUi();', clearProtectedDom);
assert.ok(clearProtectedDom > failClosedStart && failClosedUi > clearProtectedDom,
    'Pageshow must clear protected DOM state before rendering fail-closed UI.');

const restoreStart = source.indexOf('const restoreAuthentication = async () =>');
const restoreEnd = source.indexOf("\n    authButton?.addEventListener", restoreStart);
const restore = source.slice(restoreStart, restoreEnd);
assert.match(restore, /setView\(location\.hash\.slice\(1\) \|\| 'dashboard', false\)/u);
assert.ok(restore.indexOf('updateAuthenticationUi();') < restore.indexOf('setView(location.hash.slice(1) || \'dashboard\', false)'),
    'The URL-selected view must be restored only after authentication UI is updated.');
assert.ok(restoreStart >= 0 && restoreEnd > restoreStart, 'Session restore lifecycle must be present.');

console.log('PASS authenticated lifecycle fault-injection contracts');
