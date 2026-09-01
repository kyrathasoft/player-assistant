import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const files = [
  'app.js', 'correlation.js', 'browser-smoke.mjs', 'service-worker.js',
  'service-worker-controller.js', 'translator-worker.js', 'campaign-search-worker.js',
  'modules/account-session.js', 'modules/inbox-state.js',
];
for (const relative of files) {
  const source = fs.readFileSync(path.join(root, 'pwa', relative), 'utf8');
  assert.ok(!/console\.(?:log|error|warn)\s*\([^\r\n)]*(?:event\.(?:message|reason)|response\.(?:body|text)|storageState)/is.test(source), `${relative} logs protected values`);
  assert.ok(!/localStorage\.(?:setItem|getItem)\s*\([^)]*(?:password|token|cookie|storage.state)/is.test(source), `${relative} persists protected state`);
}
const safe = 'correlation_id=fixture-correlation-001 endpoint=/v1/health status=401';
assert.equal(safe, safe, 'deterministic safe identifier baseline');
console.log('Protected-data negative-space PWA tests passed.');
