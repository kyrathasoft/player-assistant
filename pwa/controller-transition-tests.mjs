import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { createControllerChangeHandler } from './service-worker-controller.js';

const appSource = await readFile(new URL('./app.js', import.meta.url), 'utf8');
const browserSmokeSource = await readFile(new URL('./browser-smoke.mjs', import.meta.url), 'utf8');

const testFirstControllerAcquisitionDoesNotReload = () => {
    let controller = null;
    let reloads = 0;
    const onControllerChange = createControllerChangeHandler({
        getController: () => controller,
        reload: () => { reloads++; }
    });

    controller = { id: 'worker-1' };
    assert.equal(onControllerChange(), false);
    assert.equal(reloads, 0);
};

const testLaterControllerChangeReloadsExactlyOnce = () => {
    let controller = { id: 'worker-1' };
    let reloads = 0;
    const onControllerChange = createControllerChangeHandler({
        getController: () => controller,
        reload: () => { reloads++; }
    });

    controller = { id: 'worker-2' };
    assert.equal(onControllerChange(), true);
    assert.equal(onControllerChange(), false);
    controller = { id: 'worker-3' };
    assert.equal(onControllerChange(), false);
    assert.equal(reloads, 1);
};

const testLifecycleHandlesOfflineOnlineAndExplicitUpdateFlow = () => {
    assert.match(appSource, /createControllerChangeHandler/u);
    assert.match(appSource, /pendingServiceWorker\?\.postMessage\(\{ type: 'SKIP_WAITING' \}\)/u);
    assert.match(appSource, /registration\.addEventListener\('updatefound'/u);
    assert.match(appSource, /worker\.state === 'installed' && navigator\.serviceWorker\.controller/u);
    assert.match(browserSmokeSource, /context\.setOffline\(true\)/u);
    assert.match(browserSmokeSource, /context\.setOffline\(false\)/u);
};

testFirstControllerAcquisitionDoesNotReload();
testLaterControllerChangeReloadsExactlyOnce();
testLifecycleHandlesOfflineOnlineAndExplicitUpdateFlow();
console.log('Controller transition tests passed.');
