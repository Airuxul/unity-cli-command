import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import { ping } from './command.js';

/** Must match Unity `ConnectorPorts` defaults. */
export const DEFAULT_EDITOR_PORT = 6547;
export const DEFAULT_EDITOR_PLAY_PORT = 6794;
export const DEFAULT_PLAYER_PORT = 6795;

const PROFILES_DIR = path.join(os.homedir(), '.unity-cmd', 'profiles');

/** Integration step hostKind → profile file name. */
export const PROFILE_BY_HOST_KIND = {
  editor: 'editor',
  editor_play: 'editor-play',
  player: 'package-play',
};

/** @param {string} [kind] */
const VALID_HOST_KINDS = new Set(['editor', 'editor_play', 'player']);

export function normalizeHostKind(kind) {
  const k = (kind ?? 'editor').toLowerCase();
  if (!VALID_HOST_KINDS.has(k)) {
    throw Object.assign(new Error(`Invalid host kind: ${kind}`), { error_code: 'INVALID_HOST_KIND' });
  }
  return k;
}

/** @param {string} expected @param {string} [actual] */
export function hostKindMatches(expected, actual) {
  if (!actual) return false;
  return normalizeHostKind(expected) === normalizeHostKind(actual);
}

export function sleep(ms) {
  return new Promise((r) => setTimeout(r, ms));
}

/** Health probe: up to 3 attempts, 3s apart (see ping). */
export const PING_MAX_ATTEMPTS = 3;
export const PING_RETRY_INTERVAL_MS = 3000;

export function defaultPortForHostKind(hostKind = 'editor') {
  switch (normalizeHostKind(hostKind)) {
    case 'editor_play':
      return DEFAULT_EDITOR_PLAY_PORT;
    case 'player':
      return DEFAULT_PLAYER_PORT;
    default:
      return DEFAULT_EDITOR_PORT;
  }
}

function profileFile(name) {
  return path.join(PROFILES_DIR, `${name.replace(/[\\/:*?"<>|]/g, '_')}.json`);
}

function sanitizeProfileName(name) {
  const trimmed = String(name ?? '').trim();
  if (!trimmed) throw new Error('profile name is required');
  return trimmed.replace(/[\\/:*?"<>|]/g, '_');
}

/** @returns {object|null} */
export function loadProfile(name) {
  const file = profileFile(name);
  if (!fs.existsSync(file)) return null;
  try {
    return JSON.parse(fs.readFileSync(file, 'utf8'));
  } catch {
    return null;
  }
}

/** @param {string} name @param {{ host: string, port: number, connector_host: string }} target */
export function saveProfile(name, target) {
  fs.mkdirSync(PROFILES_DIR, { recursive: true });
  const payload = {
    name: sanitizeProfileName(name),
    host: target.host,
    port: target.port,
    connector_host: normalizeHostKind(target.connector_host ?? 'editor'),
    updated_at: new Date().toISOString(),
  };
  fs.writeFileSync(profileFile(name), JSON.stringify(payload, null, 2));
  return payload;
}

/** @param {string} name @param {{ host?: string, port?: number, connector_host?: string }} patch */
export function updateProfile(name, patch) {
  const existing = loadProfile(name);
  if (!existing) {
    throw Object.assign(new Error(`Profile not found: ${name}`), { error_code: 'PROFILE_NOT_FOUND' });
  }
  return saveProfile(name, {
    host: patch.host ?? existing.host,
    port: patch.port ?? existing.port,
    connector_host: patch.connector_host ?? existing.connector_host,
  });
}

export function deleteProfile(name) {
  const file = profileFile(name);
  if (!fs.existsSync(file)) {
    throw Object.assign(new Error(`Profile not found: ${name}`), { error_code: 'PROFILE_NOT_FOUND' });
  }
  fs.unlinkSync(file);
  return { name: sanitizeProfileName(name), deleted: true };
}

export function listProfiles() {
  if (!fs.existsSync(PROFILES_DIR)) return [];
  return fs
    .readdirSync(PROFILES_DIR)
    .filter((f) => f.endsWith('.json'))
    .map((f) => {
      try {
        return JSON.parse(fs.readFileSync(path.join(PROFILES_DIR, f), 'utf8'));
      } catch {
        return null;
      }
    })
    .filter(Boolean);
}

export function profileNameForHostKind(hostKind) {
  return PROFILE_BY_HOST_KIND[normalizeHostKind(hostKind)] ?? normalizeHostKind(hostKind);
}

export function isTransientError(err) {
  if (!err) return false;
  const msg = String(err.message ?? err).toLowerCase();
  if (err.name === 'AbortError') return true;
  return (
    msg.includes('abort') ||
    msg.includes('econnrefused') ||
    msg.includes('econnreset') ||
    msg.includes('fetch failed') ||
    msg.includes('network') ||
    msg.includes('timed out')
  );
}

export async function withRetry(fn, { timeoutMs = 20_000, intervalMs = 400 } = {}) {
  const deadline = Date.now() + timeoutMs;
  let lastError;
  while (Date.now() < deadline) {
    try {
      return await fn(Math.max(500, deadline - Date.now()));
    } catch (err) {
      lastError = err;
      if (!isTransientError(err)) throw err;
      await sleep(Math.min(intervalMs, Math.max(100, deadline - Date.now())));
    }
  }
  throw lastError ?? new Error('connection_retry_exhausted');
}

async function probeHealth(host, port, timeoutMs) {
  return ping(
    { host, port },
    {
      timeoutMs,
      retryOnDisconnect: true,
      maxAttempts: PING_MAX_ATTEMPTS,
      retryIntervalMs: PING_RETRY_INTERVAL_MS,
    },
  );
}

/**
 * Resolve target from a saved profile only.
 * @param {{ profile: string, timeoutMs?: number, verify?: boolean }} options
 */
export async function resolveTarget({ profile, timeoutMs = 20_000, verify = true } = {}) {
  if (!profile) return null;

  const saved = loadProfile(profile);
  if (!saved?.host || !saved?.port) return null;

  const expectedKind = normalizeHostKind(saved.connector_host ?? 'editor');
  const base = {
    profile: sanitizeProfileName(profile),
    host: saved.host,
    port: saved.port,
    connector_host: expectedKind,
  };

  if (!verify) return base;

  try {
    const res = await probeHealth(saved.host, saved.port, timeoutMs);
    if (res.ok && hostKindMatches(expectedKind, res.data?.host)) {
      return {
        ...base,
        connector_host: res.data?.host ?? expectedKind,
        connector_build: res.data?.connector_build,
      };
    }
  } catch {
    // unreachable
  }
  return null;
}

/** Poll until profile endpoint responds or timeout (integration tests). */
export async function waitForInstance({ profile, timeoutMs = 20_000, logProgress = true } = {}) {
  const profileName = profile ?? process.env.UNITY_CMD_PROFILE ?? null;
  if (!profileName) return null;

  const deadline = Date.now() + timeoutMs;
  let attempts = 0;
  while (Date.now() < deadline) {
    attempts += 1;
    if (logProgress && (attempts === 1 || attempts % 5 === 0)) {
      const remaining = Math.max(0, deadline - Date.now());
      console.log(`[connection] waiting profile=${profileName}, attempt=${attempts}, remaining=${remaining}ms`);
    }
    const target = await resolveTarget({
      profile: profileName,
      timeoutMs: Math.min(5_000, deadline - Date.now()),
    });
    if (target?.host && target?.port) return target;
    await sleep(300);
  }
  return resolveTarget({ profile: profileName, timeoutMs: 3_000 });
}
