import { test } from 'node:test';
import assert from 'node:assert/strict';
import { PING_MAX_ATTEMPTS, PING_RETRY_INTERVAL_MS } from '../../src/client/connection.js';

test('ping retry constants', () => {
  assert.equal(PING_MAX_ATTEMPTS, 3);
  assert.equal(PING_RETRY_INTERVAL_MS, 3000);
});
