import { test } from 'node:test';
import assert from 'node:assert/strict';
import { pollJob } from '../../src/client/job.js';

const originalFetch = global.fetch;

test('pollJob succeeds when job completes', async () => {
  let calls = 0;
  global.fetch = async () => {
    calls++;
    const body =
      calls < 2
        ? { status: 'running' }
        : { status: 'succeeded', result: { ok: true } };
    return {
      ok: true,
      status: 200,
      text: async () => JSON.stringify(body),
    };
  };

  const res = await pollJob('http://127.0.0.1:6400', 'job1', 5000);
  assert.equal(res.ok, true);
  global.fetch = originalFetch;
});

test('pollJob times out', async () => {
  global.fetch = async () => ({
    ok: true,
    status: 200,
    text: async () => JSON.stringify({ status: 'running' }),
  });

  const res = await pollJob('http://127.0.0.1:6400', 'job1', 300);
  assert.equal(res.ok, false);
  assert.equal(res.timedOut, true);
  global.fetch = originalFetch;
});
