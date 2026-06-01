import { sendCommand, ping, fetchCatalog } from './client/command.js';
import { resolveTarget } from './client/connection.js';
import { resolveTimeoutMs } from './timeout.js';
import {
  loadCatalog,
  resolveRemoteCommand,
  checkScopeCompatibility,
  cachePathForTarget,
  CATALOG_TTL_MS,
  isCatalogExpired,
} from './catalog.js';
import { coerceParameters } from './params.js';
import { cliError, enrichFailure, enrichThrown } from './errors.js';
import { runHelp } from './help.js';
import { runProfileCommand } from './profile.js';

export function parseArgs(argv) {
  const args = [...argv];
  let timeoutMs;
  const flags = {};
  const positional = [];

  for (let i = 0; i < args.length; i++) {
    const a = args[i];
    if (a === '--timeout' && args[i + 1]) {
      timeoutMs = Number.parseInt(args[++i], 10);
      continue;
    }
    if (a.startsWith('--timeout=')) {
      timeoutMs = Number.parseInt(a.split('=')[1], 10);
      continue;
    }
    if (a.startsWith('--')) {
      const key = a.slice(2);
      const next = args[i + 1];
      if (next && !next.startsWith('--')) {
        flags[key] = next;
        i++;
      } else {
        flags[key] = true;
      }
      continue;
    }
    positional.push(a);
  }

  return { positional, flags, timeoutMs: resolveTimeoutMs(timeoutMs) };
}

function requireProfile(flags) {
  const profile = flags.profile ?? process.env.UNITY_CMD_PROFILE ?? null;
  if (!profile) {
    printJson(
      cliError(
        'Profile is required. Use --profile <name> or set UNITY_CMD_PROFILE.',
        'NO_PROFILE',
        'Create one: unity-cmd profile create editor --host 127.0.0.1 --port 6547 --host-kind editor',
      ),
    );
    process.exit(1);
  }
  return profile;
}

export async function runCommand(command, flags, timeoutMs, subArgs = []) {
  if (command === 'help') {
    await runHelp(flags, timeoutMs);
    return;
  }
  if (command === 'profile') {
    await runProfileCommand(subArgs, flags, timeoutMs);
    return;
  }
  const profileName = requireProfile(flags);
  const target = await resolveTarget({ profile: profileName, timeoutMs });
  if (!target) {
    printJson(
      cliError(
        `Profile "${profileName}" is unreachable or /health host mismatch.`,
        'NO_INSTANCE',
        `Check the profile file or run: unity-cmd profile show ${profileName}`,
      ),
    );
    process.exit(1);
  }

  if (command === 'ping') {
    const res = await ping(target, { timeoutMs, retryOnDisconnect: true });
    printJson(res.ok ? { ...res, target, profile: profileName } : enrichFailure(res));
    process.exit(res.ok ? 0 : 1);
  }

  if (command === 'list') {
    try {
      const forceRefresh = flags['refresh-catalog'] === true;
      const catalog = await loadCatalog(target, { timeoutMs, forceRefresh });
      printJson({
        ok: true,
        profile: profileName,
        target: { host: target.host, port: target.port, connector_host: target.connector_host },
        cache_path: cachePathForTarget(target),
        updated_at: catalog.updated_at,
        expires_at: catalog.expires_at,
        catalog_ttl_ms: CATALOG_TTL_MS,
        catalog_expired: isCatalogExpired(catalog),
        catalog_version: catalog.catalog_version,
        connector_build: catalog.connector_build,
        commands: catalog.commands,
        alias_to_command: catalog.alias_to_command,
      });
      process.exit(0);
    } catch (err) {
      printJson(enrichThrown(err));
      process.exit(1);
    }
  }

  const forceCatalogRefresh = flags['refresh-catalog'] === true;

  let parameters = coerceParameters({ ...flags });
  delete parameters.profile;
  delete parameters['refresh-catalog'];

  let catalog;
  try {
    catalog = await loadCatalog(target, {
      timeoutMs,
      forceRefresh: forceCatalogRefresh,
    });
  } catch (err) {
    printJson(enrichThrown(err, { command }));
    process.exit(1);
  }

  const resolved = resolveRemoteCommand(command, parameters, catalog);
  const entry = catalog?.commands_by_name?.[resolved.command];
  if (entry?.scope && entry.scope !== 'any') {
    const scopeError = checkScopeCompatibility(
      resolved.command,
      entry.scope,
      target.connector_host,
    );
    if (scopeError) {
      printJson(cliError(scopeError, 'SCOPE_MISMATCH', null, { command: resolved.command }));
      process.exit(1);
    }
  }

  const effectiveTimeout =
    resolved.minTimeoutMs != null
      ? Math.max(timeoutMs, resolved.minTimeoutMs)
      : timeoutMs;

  try {
    const res = await sendCommand(target, resolved.command, parameters, {
      timeoutMs: effectiveTimeout,
      allowConnectionRetry: resolved.allowConnectionRetry,
    });
    const out = res.ok
      ? res
      : enrichFailure(res, {
          command: resolved.command,
          status: res.status,
          deferred: Boolean(res.command_id),
        });
    printJson(out);
    process.exit(res.ok ? 0 : 1);
  } catch (err) {
    printJson(enrichThrown(err, { command: resolved.command }));
    process.exit(1);
  }
}

function printJson(obj) {
  console.log(JSON.stringify(obj, null, 2));
}
