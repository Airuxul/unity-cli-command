/** CLI command names that trigger Unity script recompilation. */
export const RECOMPILE_ALIASES = new Set([
  'recompile',
  'reload',
  'reload-scripts',
  'editor.recompile',
]);

export const DEFAULT_RECOMPILE_TIMEOUT_MS = 120_000;

/**
 * Map user-facing recompile aliases to connector `compile` job command.
 * @returns {{ command: string, allowConnectionRetry: boolean, minTimeoutMs: number | null }}
 */
export function resolveRemoteCommand(command, flags = {}) {
  const normalized = command?.toLowerCase?.() ?? command;

  if (RECOMPILE_ALIASES.has(normalized) || RECOMPILE_ALIASES.has(command)) {
    return {
      command: 'compile',
      allowConnectionRetry: true,
      minTimeoutMs: DEFAULT_RECOMPILE_TIMEOUT_MS,
    };
  }

  const allowConnectionRetry =
    command === 'compile' ||
    command === 'editor.recompile' ||
    (command === 'refresh' && (flags.compile === true || flags.compile === 'true'));

  const jobCommands = new Set([
    'compile',
    'editor.recompile',
    'editor.play',
    'editor.stop',
  ]);

  return {
    command,
    allowConnectionRetry:
      allowConnectionRetry || jobCommands.has(command),
    minTimeoutMs:
      command === 'compile' || command === 'editor.recompile'
        ? DEFAULT_RECOMPILE_TIMEOUT_MS
        : command === 'editor.play' || command === 'editor.stop'
          ? 60_000
          : null,
  };
}
