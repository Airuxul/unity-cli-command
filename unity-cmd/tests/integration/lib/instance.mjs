import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';

export function getInstancesDir() {
  return path.join(os.homedir(), '.unity-cmd', 'instances');
}

export function listInstances() {
  const dir = getInstancesDir();
  if (!fs.existsSync(dir)) return [];
  return fs
    .readdirSync(dir)
    .filter((f) => f.endsWith('.json'))
    .map((f) => {
      try {
        return JSON.parse(fs.readFileSync(path.join(dir, f), 'utf8').replace(/^\uFEFF/, ''));
      } catch {
        return null;
      }
    })
    .filter(Boolean);
}

export function selectInstance(projectHint) {
  const instances = listInstances();
  if (instances.length === 0) return null;

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

export async function waitForInstanceAsync({ projectHint, timeoutMs }) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    const inst = selectInstance(projectHint);
    if (inst?.host && inst?.port) return inst;
    await new Promise((r) => setTimeout(r, 200));
  }
  return selectInstance(projectHint);
}

export function readManifestEditorState(instance) {
  return instance?.editor_state ?? {};
}
