import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { waitForInstanceAsync } from './lib/instance.mjs';
import { runStep } from './lib/steps.mjs';

function sleep(ms) {
  return new Promise((r) => setTimeout(r, ms));
}

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const OUT_DIR = path.join(__dirname, 'out');
const SCENARIO = path.join(__dirname, 'scenarios', 'full-lifecycle.json');
const ATTACH_TIMEOUT_MS = 20_000;

async function main() {
  const projectHint = process.env.UNITY_CMD_PROJECT ?? null;
  const instance = await waitForInstanceAsync({
    projectHint,
    timeoutMs: ATTACH_TIMEOUT_MS,
  });

  if (!instance?.host || !instance?.port) {
    logSkip(projectHint);
    writeReport({ skipped: true, reason: 'no_instance' });
    process.exit(0);
  }

  const scenario = JSON.parse(fs.readFileSync(SCENARIO, 'utf8'));
  const results = [];
  let failed = false;

  for (const step of scenario.steps) {
    const timeoutMs = step.timeoutMs ?? 20_000;
    const stepStarted = Date.now();
    try {
      const result = await Promise.race([
        runStep(step, instance, timeoutMs),
        timeoutAfter(step.name, timeoutMs),
      ]);
      results.push(result);
      if (result.status !== 'passed') failed = true;
      console.log(`[${result.status}] ${result.name} (${result.elapsedMs}ms)`);
      if (result.error) console.error(`  ${result.error}`);
    } catch (err) {
      failed = true;
      const isTimeout = String(err.message).includes('exceeded');
      const result = {
        name: step.name,
        status: isTimeout ? 'timeout' : 'failed',
        elapsedMs: Date.now() - stepStarted,
        error: err.message,
      };
      results.push(result);
      console.log(`[${result.status}] ${step.name}`);
      console.error(`  ${err.message}`);
    }

    if (failed) break;
    await sleep(300);
  }

  writeReport({ skipped: false, instance: { host: instance.host, port: instance.port }, results });
  process.exit(failed ? 1 : 0);
}

function timeoutAfter(name, ms) {
  return new Promise((_, reject) =>
    setTimeout(() => reject(new Error(`step ${name} exceeded ${ms}ms`)), ms),
  );
}

function logSkip(projectHint) {
  console.error('[integration] 未检测到可用的 Unity Editor 实例。');
  console.error('  - 请在外部工程中安装 unity-connector 并用 Unity 打开该项目');
  if (projectHint) console.error(`  - UNITY_CMD_PROJECT=${projectHint}`);
  else console.error('  - 可设置 UNITY_CMD_PROJECT 指向工程路径（多实例时必需）');
  console.error('  - 确认 ~/.unity-cmd/instances/ 下已有 heartbeat');
  console.error('  - 跳过集成测试（非失败）');
}

function writeReport(report) {
  fs.mkdirSync(OUT_DIR, { recursive: true });
  fs.writeFileSync(path.join(OUT_DIR, 'report.json'), JSON.stringify(report, null, 2));
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
