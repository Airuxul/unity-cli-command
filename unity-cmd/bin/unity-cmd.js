#!/usr/bin/env node
import { parseArgs, runCommand } from '../src/dispatch.js';

const { positional, flags, timeoutMs } = parseArgs(process.argv.slice(2));
const command = positional[0];

if (!command) {
  console.error('Usage: unity-cmd <command> [options]');
  process.exit(1);
}

await runCommand(command, flags, timeoutMs);
