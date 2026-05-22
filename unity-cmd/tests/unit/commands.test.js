import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  resolveRemoteCommand,
  RECOMPILE_ALIASES,
  DEFAULT_RECOMPILE_TIMEOUT_MS,
} from '../../src/commands.js';

test('recompile aliases map to compile job', () => {
  for (const alias of RECOMPILE_ALIASES) {
    const r = resolveRemoteCommand(alias);
    assert.equal(r.command, 'compile');
    assert.equal(r.allowConnectionRetry, true);
    assert.equal(r.minTimeoutMs, DEFAULT_RECOMPILE_TIMEOUT_MS);
  }
});

test('compile gets long timeout and retry', () => {
  const r = resolveRemoteCommand('compile');
  assert.equal(r.command, 'compile');
  assert.equal(r.allowConnectionRetry, true);
  assert.equal(r.minTimeoutMs, DEFAULT_RECOMPILE_TIMEOUT_MS);
});

test('ping is unchanged', () => {
  const r = resolveRemoteCommand('ping');
  assert.equal(r.command, 'ping');
  assert.equal(r.allowConnectionRetry, false);
  assert.equal(r.minTimeoutMs, null);
});

test('editor.play gets retry and 60s min timeout', () => {
  const r = resolveRemoteCommand('editor.play');
  assert.equal(r.allowConnectionRetry, true);
  assert.equal(r.minTimeoutMs, 60_000);
});
