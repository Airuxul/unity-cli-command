import { requestJson } from './http.js';
import { pollCommandStatus } from './command-status.js';
import { resolveTimeoutMs } from '../timeout.js';
import { enrichFailure } from '../errors.js';
import { sleep, PING_MAX_ATTEMPTS, PING_RETRY_INTERVAL_MS } from './connection.js';

export { PING_MAX_ATTEMPTS, PING_RETRY_INTERVAL_MS };

export async function sendCommand(target, command, parameters = {}, options = {}) {
  options.command = command;
  const timeoutMs = resolveTimeoutMs(options.timeoutMs);
  const baseUrl = `http://${target.host}:${target.port}`;
  const body = {
    command,
    parameters,
    request_id: options.requestId,
  };

  const retry = options.allowConnectionRetry !== false;

  const { status, data } = await requestJson(`${baseUrl}/command`, {
    method: 'POST',
    body,
    timeoutMs,
    retryOnDisconnect: retry,
  });

  const commandId = data?.command_id;
  if (status === 202 && commandId) {
    const polled = await pollCommandStatus(baseUrl, commandId, timeoutMs, {
      allowConnectionRetry: retry,
    });
    const result = {
      ok: polled.ok,
      status: polled.ok ? 200 : 500,
      data: polled.data?.result ?? polled.data,
      error: polled.error,
      command_id: commandId,
      request_id: data.request_id,
      timedOut: polled.timedOut,
    };
    return polled.ok ? result : enrichFailure(result, { deferred: true, status: result.status });
  }

  const result = {
    ok: data?.ok ?? (status >= 200 && status < 300),
    status,
    data: data?.data,
    error: data?.error,
    request_id: data?.request_id,
  };
  return result.ok ? result : enrichFailure(result, { status, command: options.command });
}

async function pingOnce(target, options = {}) {
  const timeoutMs = resolveTimeoutMs(options.timeoutMs);
  const baseUrl = `http://${target.host}:${target.port}`;
  const { status, data } = await requestJson(`${baseUrl}/health`, {
    timeoutMs,
    retryOnDisconnect: false,
  });
  return { ok: status === 200 && data?.ok, status, data };
}

/** @param {{ timeoutMs?: number, retryOnDisconnect?: boolean, maxAttempts?: number, retryIntervalMs?: number }} options */
export async function ping(target, options = {}) {
  const maxAttempts =
    options.maxAttempts ??
    (options.retryOnDisconnect === false ? 1 : PING_MAX_ATTEMPTS);
  const intervalMs = options.retryIntervalMs ?? PING_RETRY_INTERVAL_MS;

  let last;
  for (let attempt = 1; attempt <= maxAttempts; attempt++) {
    last = await pingOnce(target, options);
    if (last.ok) return last;
    if (attempt < maxAttempts) await sleep(intervalMs);
  }
  return last;
}

export async function fetchCatalog(target, options = {}) {
  const timeoutMs = resolveTimeoutMs(options.timeoutMs);
  const baseUrl = `http://${target.host}:${target.port}`;
  const { status, data } = await requestJson(`${baseUrl}/list`, {
    method: 'POST',
    body: {},
    timeoutMs,
    retryOnDisconnect: options.retryOnDisconnect !== false,
  });
  return {
    ok: status === 200 && data?.ok !== false,
    status,
    catalog_version: data?.catalog_version ?? null,
    connector_build: data?.connector_build ?? null,
    commands: data?.commands ?? [],
    alias_to_command: data?.alias_to_command ?? {},
  };
}
