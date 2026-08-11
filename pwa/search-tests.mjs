import assert from 'node:assert/strict';
import {
    createSearchExpression,
    createSearchExpressionCache
} from './modules/search.js';

let buildCount = 0;
const getCachedExpression = createSearchExpressionCache((term) => {
    buildCount += 1;
    return createSearchExpression(term);
});

const first = getCachedExpression('silver*');
const second = getCachedExpression('silver*');
assert.strictEqual(second, first);
assert.equal(buildCount, 1);
assert.equal(first.test('silver moon'), true);

const different = getCachedExpression('sun');
assert.notStrictEqual(different, first);
assert.equal(buildCount, 2);
assert.equal(different.test('silver moon'), false);

console.log('Campaign search expression-cache tests passed.');
