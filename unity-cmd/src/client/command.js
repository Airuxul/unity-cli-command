import { requestJson } from './http.js';
import { pollJob } from './job.js';
import { resolveTimeoutMs } from '../timeout.js';

export async function sendCommand(target, command, parameters = {}, options = {}) {
  const timeoutMs = resolveTimeoutMs(options.timeoutMs);
  const baseUrl = `http://${target.host}:${target.port}`;
  const body = {
    command,
    parameters,
    request_id: options.requestId,
  };

  const { status, data } = await requestJson(`${baseUrl}/command`, {
    method: 'POST',
    body,
    timeoutMs,
  });

  if (status === 202 && data?.job_id) {
    const polled = await pollJob(baseUrl, data.job_id, timeoutMs, {
      // Play/compile/stop often domain-reload; retry unless explicitly disabled.
      allowConnectionRetry: options.allowConnectionRetry !== false,
    });
    return {
      ok: polled.ok,
      status: polled.ok ? 200 : 500,
      data: polled.data?.result ?? polled.data,
      error: polled.error,
      job_id: data.job_id,
      request_id: data.request_id,
    };
  }

  return {
    ok: data?.ok ?? (status >= 200 && status < 300),
    status,
    data: data?.data,
    error: data?.error,
    request_id: data?.request_id,
  };
}

export async function ping(target, options = {}) {
  const timeoutMs = resolveTimeoutMs(options.timeoutMs);
  const baseUrl = `http://${target.host}:${target.port}`;
  const { status, data } = await requestJson(`${baseUrl}/health`, { timeoutMs });
  return { ok: status === 200 && data?.ok, status, data };
}

export async function listCommands(target, options = {}) {
  const timeoutMs = resolveTimeoutMs(options.timeoutMs);
  const baseUrl = `http://${target.host}:${target.port}`;
  const { status, data } = await requestJson(`${baseUrl}/list`, {
    method: 'POST',
    body: {},
    timeoutMs,
  });
  return { ok: status === 200, data: data?.commands ?? [], status };
}
