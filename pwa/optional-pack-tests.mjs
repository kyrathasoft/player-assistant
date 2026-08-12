import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';

const root = new URL('.', import.meta.url);
const manifest = JSON.parse(await readFile(new URL('./optional-packs.json', root), 'utf8'));
const loader = await readFile(new URL('./optional-pack-loader.js', root), 'utf8');
const serviceWorker = await readFile(new URL('./service-worker.js', root), 'utf8');
assert.match(loader, /crypto\.subtle/u);
assert.match(loader, /attempts = 3/u);
assert.match(loader, /setTimeout\(resolve, 250 \* \(2 \*\* attempt\)\)/u);
assert.match(loader, /Content-Type/u);
assert.match(loader, /cache\.put\(cacheKey, validated\.response\.clone\(\)\)/u);
assert.match(loader, /QuotaExceededError/u);
assert.match(loader, /removePack/u);

assert.equal(manifest.packs.length, 4);
assert.equal(new Set(manifest.packs.map((pack) => pack.id)).size, 4);
assert.match(serviceWorker, /optional-packs\.json/u);
assert.match(serviceWorker, /optional-pack-loader\.js/u);
assert.doesNotMatch(serviceWorker, /data\/(?:orcish|elvish|ghukliak)\.json'[\s\S]{0,100}OFFLINE_DATA_ASSETS/u);

for (const pack of manifest.packs) {
    const bytes = await readFile(new URL(`./${pack.url}`, root));
    assert.equal(bytes.byteLength, pack.byteSize, `${pack.id} byte size`);
    assert.equal(createHash('sha256').update(bytes).digest('hex'), pack.contentHash, `${pack.id} hash`);
    const payload = JSON.parse(bytes);
    assert.equal(payload.schemaVersion, pack.schemaVersion);
    assert.equal(payload[pack.kind === 'translator' ? 'entryCount' : 'pageCount'], pack.recordCount);
}

console.log('Optional-pack manifest and install-shell contracts passed.');
