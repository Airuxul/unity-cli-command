import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import { fetchCatalog, ping } from './client/command.js';

const CACHE_DIR = path.join(os.homedir(), '.unity-cmd', 'cache');

/** Agent skill TTL: catalog older than this is treated as expired. */
export const CATALOG_TTL_MS = 24 * 60 * 60 * 1000;

export function cachePathForTarget(target) {
  const key = `${target.host}:${target.port}`.replace(/[\\/:*?"<>|]/g, '_');
  return path.join(CACHE_DIR, `catalog-${key}.json`);
}

/** Read cached catalog from disk without validation. */
export function readCachedCatalog(target) {
  const cachePath = cachePathForTarget(target);
  if (!fs.existsSync(cachePath)) return null;
  try {
    return indexCatalog(JSON.parse(fs.readFileSync(cachePath, 'utf8')));
  } catch {
    return null;
  }
}

/**
 * @param {object} raw
 * @returns {import('./catalog.js').CatalogIndex}
 */
export function indexCatalog(raw) {
  const commands = raw?.commands ?? [];
  const commandsByName = Object.create(null);
  for (const entry of commands) {
    if (entry?.name) commandsByName[entry.name] = entry;
  }
  return {
    host_kind: raw?.host_kind ?? null,
    catalog_version: raw?.catalog_version ?? null,
    connector_build: raw?.connector_build ?? null,
    updated_at: raw?.updated_at ?? null,
    expires_at: raw?.expires_at ?? null,
    commands,
    commands_by_name: commandsByName,
    alias_to_command: raw?.alias_to_command ?? {},
  };
}

/** @param {Date} [now] */
export function catalogTimestamps(now = new Date()) {
  const updated_at = now.toISOString();
  const expires_at = new Date(now.getTime() + CATALOG_TTL_MS).toISOString();
  return { updated_at, expires_at };
}

/**
 * @param {object | null | undefined} catalog
 * @param {number} [nowMs]
 */
export function isCatalogExpired(catalog, nowMs = Date.now()) {
  if (!catalog) return true;
  if (catalog.expires_at) {
    const exp = Date.parse(catalog.expires_at);
    if (!Number.isNaN(exp)) return nowMs >= exp;
  }
  if (catalog.updated_at) {
    const updated = Date.parse(catalog.updated_at);
    if (!Number.isNaN(updated)) return nowMs >= updated + CATALOG_TTL_MS;
  }
  return true;
}

/**
 * @param {object} cached
 * @param {object} target
 * @param {number} timeoutMs
 */
export async function isCacheValid(cached, target, timeoutMs) {
  if (!cached?.commands?.length || !cached?.catalog_version) return false;
  if (isCatalogExpired(cached)) return false;

  const health = await ping(target, {
    timeoutMs: Math.min(timeoutMs, 5000),
    retryOnDisconnect: true,
  });
  if (!health.ok) return false;

  const liveBuild = health.data?.connector_build;
  if (
    cached.connector_build != null &&
    liveBuild != null &&
    cached.connector_build !== liveBuild
  ) {
    return false;
  }

  const liveCatalogVersion = health.data?.catalog_version;
  if (
    cached.catalog_version &&
    liveCatalogVersion &&
    cached.catalog_version !== liveCatalogVersion
  ) {
    return false;
  }

  const cachedHost = cached.host_kind?.toLowerCase?.();
  const targetHost = target.connector_host?.toLowerCase?.();
  if (cachedHost && targetHost && cachedHost !== targetHost) {
    return false;
  }

  return true;
}

export async function loadCatalog(target, options = {}) {
  const { forceRefresh = false, timeoutMs } = options;
  const cachePath = cachePathForTarget(target);

  if (!forceRefresh && fs.existsSync(cachePath)) {
    try {
      const cached = JSON.parse(fs.readFileSync(cachePath, 'utf8'));
      if (await isCacheValid(cached, target, timeoutMs)) {
        return indexCatalog(cached);
      }
    } catch {
      // refresh below
    }
  }

  const health = await ping(target, { timeoutMs, retryOnDisconnect: true });
  if (!health.ok) {
    throw Object.assign(
      new Error('Failed to reach Unity Editor HTTP endpoint.'),
      {
        error_code: 'CONNECTION_FAILED',
        hint: 'Open the project in Unity and run unity-cmd ping.',
      },
    );
  }

  const res = await fetchCatalog(target, { timeoutMs, retryOnDisconnect: true });
  if (!res.ok) {
    throw Object.assign(new Error('Failed to fetch command catalog from Unity Editor.'), {
      error_code: 'CATALOG_FETCH_FAILED',
      hint: 'Run unity-cmd refresh --compile true if connector scripts changed.',
    });
  }

  const payload = {
    ...catalogTimestamps(),
    host_kind: target?.connector_host ?? null,
    catalog_version: res.catalog_version,
    connector_build: health.data?.connector_build ?? null,
    commands: res.commands,
    alias_to_command: res.alias_to_command,
  };

  fs.mkdirSync(CACHE_DIR, { recursive: true });
  fs.writeFileSync(cachePath, JSON.stringify(payload, null, 2));
  return indexCatalog(payload);
}

/**
 * Mirror of Unity CommandAvailability.IsAvailableForHost.
 * Returns an error string when the command scope is incompatible with the target host,
 * or null when the command is fine to send.
 *
 * @param {string} command  canonical command name
 * @param {string} scope   'editor' | 'runtime' | 'any'
 * @param {string} hostKind  connector_host value from target
 * @returns {string|null}
 */
export function checkScopeCompatibility(command, scope, hostKind) {
  const normalized = hostKind?.toLowerCase?.() ?? '';
  const isPlayHost = normalized === 'editor_play' || normalized === 'player';

  if (isPlayHost) {
    if (scope === 'editor')
      return `Command '${command}' is Editor-only. Use --profile editor.`;
    return null;
  }

  // editor host
  if (scope === 'runtime')
    return `Command '${command}' is Runtime-only. Use --profile editor-play or --profile package-play.`;

  return null;
}

/**
 * Resolve CLI command name to remote command + polling options using Unity catalog.
 * @param {string} command
 * @param {Record<string, unknown>} flags
 * @param {ReturnType<typeof indexCatalog>} catalog
 */
export function resolveRemoteCommand(command, flags = {}, catalog) {
  const aliasMap = catalog?.alias_to_command ?? {};
  const byName = catalog?.commands_by_name ?? {};

  let canonical = command;
  const lower = command?.toLowerCase?.() ?? command;
  if (aliasMap[command]) canonical = aliasMap[command];
  else if (aliasMap[lower]) canonical = aliasMap[lower];

  const entry = byName[canonical];
  const refreshWithCompile =
    canonical === 'refresh' &&
    (flags.compile === true || flags.compile === 'true');
  const compileEntry = byName.compile;

  if (refreshWithCompile) {
    return {
      command: canonical,
      allowConnectionRetry: true,
      minTimeoutMs: compileEntry?.default_timeout_ms ?? 30_000,
    };
  }

  const minTimeoutMs =
    entry?.default_timeout_ms > 0 ? entry.default_timeout_ms : null;
  const allowConnectionRetry =
    entry?.allow_connection_retry !== false && entry?.allow_connection_retry !== 'false';

  return {
    command: canonical,
    allowConnectionRetry,
    minTimeoutMs,
  };
}
