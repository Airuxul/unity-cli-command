import { test } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import { selectInstance, getInstancesDir } from '../../src/client/target.js';

test('selectInstance returns null when empty', () => {
  const dir = path.join(os.tmpdir(), 'unity-cmd-test-instances-empty');
  const prev = process.env.UNITY_CMD_INSTANCES_DIR;
  process.env.UNITY_CMD_INSTANCES_DIR = dir;
  try {
    fs.rmSync(dir, { recursive: true, force: true });
    fs.mkdirSync(dir, { recursive: true });
    assert.equal(selectInstance('MyGame'), null);
  } finally {
    if (prev) process.env.UNITY_CMD_INSTANCES_DIR = prev;
    else delete process.env.UNITY_CMD_INSTANCES_DIR;
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

test('selectInstance matches project hint', () => {
  const dir = path.join(os.tmpdir(), 'unity-cmd-test-instances-hint');
  const prev = process.env.UNITY_CMD_INSTANCES_DIR;
  process.env.UNITY_CMD_INSTANCES_DIR = dir;
  try {
    fs.rmSync(dir, { recursive: true, force: true });
    fs.mkdirSync(dir, { recursive: true });
    const project = path.join(os.tmpdir(), 'unity-cli-test-proj');
    const file = path.join(dir, 'test-instance.json');
    fs.writeFileSync(
      file,
      JSON.stringify({
        project_path: project,
        host: '127.0.0.1',
        port: 6401,
      }),
    );

    const inst = selectInstance('unity-cli-test-proj');
    assert.equal(inst.port, 6401);
  } finally {
    if (prev) process.env.UNITY_CMD_INSTANCES_DIR = prev;
    else delete process.env.UNITY_CMD_INSTANCES_DIR;
    fs.rmSync(dir, { recursive: true, force: true });
  }
});
