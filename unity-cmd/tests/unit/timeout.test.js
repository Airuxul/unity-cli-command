import { test } from 'node:test';
import assert from 'node:assert/strict';
import { resolveTimeoutMs, DEFAULT_TIMEOUT_MS } from '../../src/timeout.js';

test('resolveTimeoutMs uses default', () => {
  const prev = process.env.UNITY_CMD_TIMEOUT_MS;
  delete process.env.UNITY_CMD_TIMEOUT_MS;
  assert.equal(resolveTimeoutMs(), DEFAULT_TIMEOUT_MS);
  if (prev) process.env.UNITY_CMD_TIMEOUT_MS = prev;
});

test('resolveTimeoutMs respects override', () => {
  assert.equal(resolveTimeoutMs(5000), 5000);
});
