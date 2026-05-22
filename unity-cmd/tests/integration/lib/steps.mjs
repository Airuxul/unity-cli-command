import { spawn } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { readManifestEditorState, listInstances } from './instance.mjs';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const CLI = path.join(__dirname, '..', '..', '..', 'bin', 'unity-cmd.js');

export function runCli(command, args, env, timeoutMs, target) {
  return new Promise((resolve, reject) => {
    const child = spawn(process.execPath, [CLI, command, ...args], {
      env: {
        ...process.env,
        ...env,
        UNITY_CMD_TIMEOUT_MS: String(timeoutMs),
        ...(target?.project_path
          ? { UNITY_CMD_PROJECT: target.project_path }
          : {}),
      },
      stdio: ['ignore', 'pipe', 'pipe'],
    });

    let stdout = '';
    let stderr = '';
    child.stdout.on('data', (d) => (stdout += d));
    child.stderr.on('data', (d) => (stderr += d));

    const timer = setTimeout(() => {
      child.kill();
      reject(new Error(`CLI timeout after ${timeoutMs}ms`));
    }, timeoutMs + 2000);

    child.on('close', (code) => {
      clearTimeout(timer);
      resolve({ code, stdout, stderr });
    });
  });
}

export async function runStep(step, target, timeoutMs) {
  const started = Date.now();

  if (step.assertManifest) {
    const instances = listInstances();
    const inst = instances.find((i) => i.port === target.port) ?? target;
    const state = readManifestEditorState(inst);
    for (const [key, expected] of Object.entries(step.assertManifest)) {
      if (state[key] !== expected) {
        return {
          name: step.name,
          status: 'failed',
          elapsedMs: Date.now() - started,
          error: `manifest.${key}: expected ${expected}, got ${state[key]}`,
        };
      }
    }
    return { name: step.name, status: 'passed', elapsedMs: Date.now() - started };
  }

  if (step.expectJob) {
    const res = await runCli(step.command, [], {}, timeoutMs, target);
    return finishCliStep(step, res, started);
  }

  const res = await runCli(step.command, [], {}, timeoutMs, target);
  return finishCliStep(step, res, started, { checkExpect: true });
}

function finishCliStep(step, res, started, { checkExpect = false } = {}) {
  let parsed;
  try {
    parsed = JSON.parse(res.stdout);
  } catch {
    return fail(
      step.name,
      started,
      `invalid json (code=${res.code}): ${res.stdout || res.stderr}`,
    );
  }

  if (res.code !== 0 || !parsed.ok) {
    return fail(step.name, started, parsed.error ?? res.stderr ?? `exit ${res.code}`);
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
      }
    }
  }

  return { name: step.name, status: 'passed', elapsedMs: Date.now() - started };
}

function fail(name, started, error) {
  return { name, status: 'failed', elapsedMs: Date.now() - started, error };
}
