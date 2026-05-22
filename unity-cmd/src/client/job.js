import { requestJson } from './http.js';
import { sleep } from './target.js';

export async function pollJob(baseUrl, jobId, timeoutMs, { allowConnectionRetry = false } = {}) {
  const deadline = Date.now() + timeoutMs;
  let lastError = null;

  while (Date.now() < deadline) {
    try {
      const { status, data } = await requestJson(`${baseUrl}/jobs/${jobId}`, {
        timeoutMs: Math.min(5_000, timeoutMs),
      });

      if (status === 404) {
        return { ok: false, error: 'job_not_found', data };
      }

      const jobStatus = data?.status;
      if (jobStatus === 'succeeded') {
        return { ok: true, data };
      }
      if (jobStatus === 'failed' || jobStatus === 'orphaned') {
        return { ok: false, error: data?.error ?? jobStatus, data };
      }
    } catch (err) {
      lastError = err;
      if (!allowConnectionRetry) throw err;
    }

    await sleep(200);
  }

  return {
    ok: false,
    error: lastError?.message ?? 'job_poll_timeout',
    timedOut: true,
  };
}
