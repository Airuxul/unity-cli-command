import { test } from 'node:test';
import assert from 'node:assert/strict';
import { coerceParameters } from '../../src/params.js';

test('coerce compile and clear flags', () => {
  assert.equal(coerceParameters({ compile: 'true' }).compile, true);
  assert.equal(coerceParameters({ clear: '1' }).clear, true);
  assert.equal(coerceParameters({ force: 'true' }).force, true);
});
