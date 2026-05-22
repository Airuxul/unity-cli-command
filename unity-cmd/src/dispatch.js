import { sendCommand, ping, listCommands } from './client/command.js';
import { selectInstance } from './client/target.js';
import { resolveTimeoutMs } from './timeout.js';
import { resolveRemoteCommand } from './commands.js';

const META = new Set(['ping', 'list', 'help']);

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

export async function runCommand(command, flags, timeoutMs) {
  const projectHint = process.env.UNITY_CMD_PROJECT ?? flags.project;
  const target = selectInstance(projectHint);
  if (!target) {
    console.error(
      'No Unity Editor instance found. Open your project with unity-connector installed.',
    );
    console.error('Set UNITY_CMD_PROJECT to disambiguate multiple instances.');
    process.exit(1);
  }

  if (command === 'ping') {
    const res = await ping(target, { timeoutMs });
    printJson(res);
    process.exit(res.ok ? 0 : 1);
  }

  if (command === 'list') {
    const res = await listCommands(target, { timeoutMs });
    printJson(res);
    process.exit(res.ok ? 0 : 1);
  }

  if (command === 'help') {
    printHelp();
    process.exit(0);
  }

  const parameters = { ...flags };
  delete parameters.project;

  const resolved = resolveRemoteCommand(command, parameters);
  const effectiveTimeout =
    resolved.minTimeoutMs != null
      ? Math.max(timeoutMs, resolved.minTimeoutMs)
      : timeoutMs;

  const res = await sendCommand(target, resolved.command, parameters, {
    timeoutMs: effectiveTimeout,
    allowConnectionRetry: resolved.allowConnectionRetry,
  });
  printJson(res);
  process.exit(res.ok ? 0 : 1);
}

function printJson(obj) {
  console.log(JSON.stringify(obj, null, 2));
}

function printHelp() {
  console.log(`unity-cmd — send commands to unity-connector

Usage:
  unity-cmd ping
  unity-cmd list
  unity-cmd recompile              # recompile scripts (waits for job, default 120s)
  unity-cmd compile | editor.recompile
  unity-cmd <command> [--key value] [--timeout ms]

Environment:
  UNITY_CMD_PROJECT       Select Editor instance by path or name
  UNITY_CMD_HOST / PORT   Override endpoint
  UNITY_CMD_TIMEOUT_MS    Default timeout (20000; recompile uses at least 120000)
`);
}
