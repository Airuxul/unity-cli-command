export const DEFAULT_TIMEOUT_MS = 20_000;

export function resolveTimeoutMs(overrideMs) {
  if (overrideMs != null && overrideMs > 0) return overrideMs;
  const env = process.env.UNITY_CMD_TIMEOUT_MS;
  if (env) {
    const parsed = Number.parseInt(env, 10);
    if (!Number.isNaN(parsed) && parsed > 0) return parsed;
  }
  return DEFAULT_TIMEOUT_MS;
}
