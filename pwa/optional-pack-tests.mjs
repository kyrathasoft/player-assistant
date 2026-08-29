import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';

const root = new URL('.', import.meta.url);
const manifest = JSON.parse(await readFile(new URL('./optional-packs.json', root), 'utf8'));
const loader = await readFile(new URL('./optional-pack-loader.js', root), 'utf8');
const serviceWorker = await readFile(new URL('./service-worker.js', root), 'utf8');
assert.ok(loader.includes('crypto.subtle'));
assert.ok(loader.includes('QuotaExceededError'));
assert.ok(loader.includes('removePack'));

assert.equal(manifest.packs.length, 4);
assert.equal(new Set(manifest.packs.map((pack) => pack.id)).size, 4);
assert.ok(serviceWorker.includes('./optional-pack-loader.js'));
assert.ok(serviceWorker.includes('./optional-packs.json'));
const installBlock = serviceWorker.match(/const SHELL_ASSETS = \[([\s\S]*?)\];/u)?.[1] || '';
assert.doesNotMatch(installBlock, /(?:orcish|elvish|ghukliak|campaign-search)\.json/u);
assert.doesNotMatch(serviceWorker, /cacheAssets\(DATA_CACHE, OFFLINE_DATA_ASSETS\)/u);

for (const pack of manifest.packs) {
    const bytes = await readFile(new URL(`./${pack.url}`, root));
    assert.equal(bytes.byteLength, pack.byteSize, `${pack.id} byte size`);
    assert.equal(createHash('sha256').update(bytes).digest('hex'), pack.contentHash, `${pack.id} hash`);
    const payload = JSON.parse(bytes);
    assert.equal(payload.schemaVersion, pack.schemaVersion);
    assert.equal(payload[pack.kind === 'translator' ? 'entryCount' : 'pageCount'], pack.recordCount);
}

console.log('Optional-pack manifest and install-shell contracts passed.');
