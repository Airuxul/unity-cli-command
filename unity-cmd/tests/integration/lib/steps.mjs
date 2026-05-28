import { spawn } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  profileNameForHostKind,
  waitForInstance,
} from '../../../src/client/connection.js';

function sleep(ms) {
  return new Promise((r) => setTimeout(r, ms));
}

function expandEnv(text) {
  if (typeof text !== 'string') return text;
  return text.replace(/\$\{([A-Z0-9_]+)\}/gi, (_, key) => process.env[key] ?? '');
}

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const CLI = path.join(__dirname, '..', '..', '..', 'bin', 'unity-cmd.js');

export function runCli(command, args = [], env, timeoutMs, profileName) {
  return new Promise((resolve, reject) => {
    const started = Date.now();
    console.log(
      `[cli:start] cmd=${command} profile=${profileName} timeout=${timeoutMs}ms args=${JSON.stringify(args)}`,
    );
    const child = spawn(process.execPath, [CLI, command, ...args], {
      env: {
        ...process.env,
        ...env,
        UNITY_CMD_PROFILE: profileName,
        UNITY_CMD_TIMEOUT_MS: String(timeoutMs),
      },
      stdio: ['ignore', 'pipe', 'pipe'],
    });

    let stdout = '';
    let stderr = '';
    child.stdout.on('data', (d) => (stdout += d));
    child.stderr.on('data', (d) => (stderr += d));

    let timeoutTriggered = false;
    const timer = setTimeout(() => {
      timeoutTriggered = true;
      console.error(
        `[cli:timeout] cmd=${command} profile=${profileName} elapsed=${Date.now() - started}ms`,
      );
      child.kill();
      reject(new Error(`CLI timeout after ${timeoutMs}ms`));
    }, timeoutMs + 2000);

    child.on('error', (err) => {
      clearTimeout(timer);
      reject(err);
    });

    child.on('close', (code) => {
      clearTimeout(timer);
      if (!timeoutTriggered) {
        console.log(
          `[cli:end] cmd=${command} profile=${profileName} code=${code} elapsed=${Date.now() - started}ms stdout=${stdout.length}B stderr=${stderr.length}B`,
        );
      }
      resolve({ code, stdout, stderr });
    });
  });
}

export async function runStep(step, defaultProfile, timeoutMs) {
  const started = Date.now();

  if (step.sleepMs != null) {
    await sleep(step.sleepMs);
    return { name: step.name, status: 'passed', elapsedMs: Date.now() - started };
  }

  if (step.waitProfile) {
    const timeout = step.timeoutMs ?? timeoutMs;
    console.log(`[wait:profile] ${step.waitProfile} timeout=${timeout}ms`);
    const target = await waitForInstance({ profile: step.waitProfile, timeoutMs: timeout });
    if (!target?.host) {
      return fail(
        step.name,
        started,
        `profile "${step.waitProfile}" not reachable within ${timeout}ms`,
      );
    }
    return { name: step.name, status: 'passed', elapsedMs: Date.now() - started };
  }

  if (step.assertFile) {
    const explicitPath = expandEnv(step.assertFile.path);
    const projectRoot = expandEnv(step.assertFile.projectRoot ?? process.env.UNITY_CMD_WORKSPACE?.trim());
    const rel = expandEnv(step.assertFile.relativePath);
    const full = explicitPath || (projectRoot && rel ? path.join(projectRoot, rel) : null);
    if (!full) {
      return fail(
        step.name,
        started,
        'assertFile requires assertFile.path or (projectRoot + relativePath) / UNITY_CMD_WORKSPACE',
      );
    }
    if (!fs.existsSync(full)) {
      return fail(step.name, started, `file not found: ${full}`);
    }
    const size = fs.statSync(full).size;
    const minBytes = step.assertFile.minBytes ?? 1;
    if (size < minBytes) {
      return fail(step.name, started, `file too small: ${size} < ${minBytes} (${full})`);
    }
    return { name: step.name, status: 'passed', elapsedMs: Date.now() - started };
  }

  const args = (step.args ?? []).map(expandEnv);
  const profile = profileForStep(step, defaultProfile);
  const res = await runCli(step.command, args, {}, timeoutMs, profile);
  return finishCliStep(step, res, started, {
    checkExpect: Boolean(step.expect),
    expectFailure: Boolean(step.expectFailure),
  });
}

function profileForStep(step, defaultProfile) {
  if (step.profile) return step.profile;
  if (step.hostKind) return profileNameForHostKind(step.hostKind);
  return defaultProfile ?? process.env.UNITY_CMD_PROFILE ?? 'editor';
}

function finishCliStep(step, res, started, { checkExpect = false, expectFailure = false } = {}) {
  let parsed;
  try {
    parsed = JSON.parse(res.stdout.trim() || '{}');
  } catch {
    if (expectFailure && res.code !== 0) {
      return { name: step.name, status: 'passed', elapsedMs: Date.now() - started };
    }
    return fail(
      step.name,
      started,
      `invalid json (code=${res.code}): ${res.stdout || res.stderr}`,
    );
  }

  if (expectFailure) {
    if (res.code === 0 && parsed.ok) {
      return fail(step.name, started, 'expected failure but command succeeded');
    }
    if (step.expectErrorCode && parsed.error_code !== step.expectErrorCode) {
      return fail(
        step.name,
        started,
        `error_code expected ${step.expectErrorCode}, got ${parsed.error_code ?? 'none'}`,
      );
    }
    return { name: step.name, status: 'passed', elapsedMs: Date.now() - started, output: parsed };
  }

  if (res.code !== 0 || !parsed.ok) {
    return fail(step.name, started, parsed.error ?? res.stderr ?? `exit ${res.code}`);
  }

  if (step.expectConnectorBuild != null) {
    const build = parsed.data?.connector_build ?? parsed.connector_build;
    if (build == null || build < step.expectConnectorBuild) {
      return fail(
        step.name,
        started,
        `connector_build expected >= ${step.expectConnectorBuild}, got ${build}`,
      );
    }
  }

  if (step.expectCatalog) {
    if (!parsed.catalog_version || typeof parsed.catalog_version !== 'string') {
      return fail(step.name, started, 'catalog_version missing');
    }
    if (!Array.isArray(parsed.commands) || parsed.commands.length === 0) {
      return fail(step.name, started, 'commands array empty');
    }
    const names = new Set(parsed.commands.map((c) => c.name));
    for (const required of step.expectCatalog.commands ?? []) {
      if (!names.has(required)) {
        return fail(step.name, started, `catalog missing command: ${required}`);
      }
    }
    for (const forbidden of step.expectCatalog.forbidden ?? []) {
      if (names.has(forbidden)) {
        return fail(step.name, started, `catalog must not include: ${forbidden}`);
      }
    }
    if (step.expectCatalog.forbiddenScopes?.length) {
      const bad = parsed.commands.filter((c) =>
        step.expectCatalog.forbiddenScopes.includes(c.scope),
      );
      if (bad.length > 0) {
        const sample = bad
          .slice(0, 5)
          .map((c) => `${c.name}(${c.scope})`)
          .join(', ');
        return fail(
          step.name,
          started,
          `catalog has forbidden scope(s) [${step.expectCatalog.forbiddenScopes.join(', ')}]: ${sample}`,
        );
      }
    }
  }

  if (checkExpect && step.expect) {
    for (const [key, expected] of Object.entries(step.expect)) {
      if (key === 'ok') {
        if (parsed.ok !== expected) {
          return fail(step.name, started, `ok !== ${expected}`);
        }
        continue;
      }
      if (key === 'data' && typeof expected === 'object') {
        for (const [dk, dv] of Object.entries(expected)) {
          if (parsed.data?.[dk] !== dv) {
            return fail(step.name, started, `data.${dk} expected ${dv}`);
          }
        }
        continue;
      }
      if (key === 'dataHas') {
        for (const dk of expected) {
          if (!(dk in (parsed.data ?? {}))) {
            return fail(step.name, started, `data missing key: ${dk}`);
          }
        }
      }
    }
  }

  return { name: step.name, status: 'passed', elapsedMs: Date.now() - started, output: parsed };
}

function fail(name, started, error) {
  return { name, status: 'failed', elapsedMs: Date.now() - started, error };
}
