import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  resolveRemoteCommand,
  catalogTimestamps,
  isCatalogExpired,
  CATALOG_TTL_MS,
} from '../../src/catalog.js';

const MOCK_CATALOG = {
  catalog_version: 'test',
  commands: [
    {
      name: 'compile',
      completion: 'compilation',
      aliases: ['recompile', 'reload', 'editor.recompile'],
      default_timeout_ms: 30000,
      allow_connection_retry: true,
    },
    {
      name: 'play',
      default_timeout_ms: 60000,
      allow_connection_retry: true,
    },
    {
      name: 'ping',
      allow_connection_retry: false,
    },
    {
      name: 'refresh',
      allow_connection_retry: false,
    },
  ],
  commands_by_name: {},
  alias_to_command: {
    recompile: 'compile',
    reload: 'compile',
    'editor.recompile': 'compile',
  },
};

for (const entry of MOCK_CATALOG.commands) {
  MOCK_CATALOG.commands_by_name[entry.name] = entry;
}

test('recompile aliases map to compile deferred command', () => {
  for (const alias of ['recompile', 'reload', 'editor.recompile']) {
    const r = resolveRemoteCommand(alias, {}, MOCK_CATALOG);
    assert.equal(r.command, 'compile');
    assert.equal(r.allowConnectionRetry, true);
    assert.equal(r.minTimeoutMs, 30_000);
  }
});

test('compile gets long timeout and retry', () => {
  const r = resolveRemoteCommand('compile', {}, MOCK_CATALOG);
  assert.equal(r.command, 'compile');
  assert.equal(r.allowConnectionRetry, true);
  assert.equal(r.minTimeoutMs, 30_000);
});

test('ping honors catalog allow_connection_retry', () => {
  const r = resolveRemoteCommand('ping', {}, MOCK_CATALOG);
  assert.equal(r.command, 'ping');
  assert.equal(r.allowConnectionRetry, false);
  assert.equal(r.minTimeoutMs, null);
});

test('play gets retry and 60s min timeout', () => {
  const r = resolveRemoteCommand('play', {}, MOCK_CATALOG);
  assert.equal(r.allowConnectionRetry, true);
  assert.equal(r.minTimeoutMs, 60_000);
});

test('editor.play alias resolves to play', () => {
  MOCK_CATALOG.alias_to_command['editor.play'] = 'play';
  const r = resolveRemoteCommand('editor.play', {}, MOCK_CATALOG);
  assert.equal(r.command, 'play');
});

test('refresh with compile flag uses compile timeout', () => {
  const r = resolveRemoteCommand('refresh', { compile: true }, MOCK_CATALOG);
  assert.equal(r.command, 'refresh');
  assert.equal(r.allowConnectionRetry, true);
  assert.equal(r.minTimeoutMs, 30_000);
});

test('catalogTimestamps sets updated_at and expires_at one TTL apart', () => {
  const now = new Date('2026-05-29T12:00:00.000Z');
  const { updated_at, expires_at } = catalogTimestamps(now);
  assert.equal(updated_at, '2026-05-29T12:00:00.000Z');
  assert.equal(expires_at, new Date(now.getTime() + CATALOG_TTL_MS).toISOString());
});

test('isCatalogExpired uses expires_at when present', () => {
  const catalog = {
    updated_at: '2026-01-01T00:00:00.000Z',
    expires_at: '2026-01-02T00:00:00.000Z',
  };
  assert.equal(isCatalogExpired(catalog, Date.parse('2026-01-01T12:00:00.000Z')), false);
  assert.equal(isCatalogExpired(catalog, Date.parse('2026-01-02T00:00:00.000Z')), true);
});

test('isCatalogExpired treats missing timestamps as expired', () => {
  assert.equal(isCatalogExpired({ commands: [] }), true);
  assert.equal(isCatalogExpired(null), true);
});

test('passes through command name when no alias map', () => {
  const empty = {
    commands: [],
    commands_by_name: {},
    alias_to_command: {},
  };
  assert.equal(resolveRemoteCommand('console', {}, empty).command, 'console');
  assert.equal(resolveRemoteCommand('play', {}, empty).command, 'play');
});
