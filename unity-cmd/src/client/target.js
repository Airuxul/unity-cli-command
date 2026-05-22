import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';

export function getInstancesDir() {
  if (process.env.UNITY_CMD_INSTANCES_DIR) {
    return process.env.UNITY_CMD_INSTANCES_DIR;
  }
  return path.join(os.homedir(), '.unity-cmd', 'instances');
}

export function listInstances() {
  const dir = getInstancesDir();
  if (!fs.existsSync(dir)) return [];

  return fs
    .readdirSync(dir)
    .filter((f) => f.endsWith('.json'))
    .map((f) => {
      const full = path.join(dir, f);
      try {
        const raw = fs.readFileSync(full, 'utf8').replace(/^\uFEFF/, '');
        return JSON.parse(raw);
      } catch {
        return null;
      }
    })
    .filter(Boolean);
}

export function selectInstance(projectHint) {
  const instances = listInstances();
  if (instances.length === 0) return null;

  if (process.env.UNITY_CMD_HOST && process.env.UNITY_CMD_PORT) {
    return {
      host: process.env.UNITY_CMD_HOST,
      port: Number.parseInt(process.env.UNITY_CMD_PORT, 10),
      project_path: projectHint ?? '',
      protocol_version: 1,
    };
  }

  if (projectHint) {
    const hint = projectHint.toLowerCase().replace(/\\/g, '/');
    const match = instances.find((i) => {
      const p = (i.project_path ?? '').toLowerCase().replace(/\\/g, '/');
      return p === hint || p.includes(hint) || path.basename(p) === path.basename(hint);
    });
    if (match) return match;
  }

  if (instances.length === 1) return instances[0];
  return null;
}

export function sleep(ms) {
  return new Promise((r) => setTimeout(r, ms));
}

export async function waitForInstanceAsync({ projectHint, timeoutMs }) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    const inst = selectInstance(projectHint);
    if (inst?.host && inst?.port) return inst;
    await sleep(200);
  }
  return selectInstance(projectHint);
}
