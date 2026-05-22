# unity-cmd — Implementation

Version: 0.1.0

## Role

Thin HTTP client and argv front-end. No Unity-specific business logic lives here.

## Modules

| Path | Responsibility |
|------|----------------|
| `src/timeout.js` | Default 20s timeout, `UNITY_CMD_TIMEOUT_MS` |
| `src/client/target.js` | Read `~/.unity-cmd/instances/*.json`, select project |
| `src/client/http.js` | `fetch` wrapper with abort |
| `src/client/job.js` | Poll `GET /jobs/{id}` until terminal state |
| `src/client/command.js` | `POST /command`, handle 200 vs 202 |
| `src/dispatch.js` | CLI routing for `ping`, `list`, `help`, arbitrary commands |
| `bin/unity-cmd.js` | Entry point |

## Request flow

```text
argv → dispatch → selectInstance (heartbeat)
              → POST /command
              → if 202: pollJob until succeeded/failed/timeout
              → print JSON, exit code
```

## Job polling

- Interval: 200ms
- Budget: `UNITY_CMD_TIMEOUT_MS` or `--timeout`
- `allowConnectionRetry`: retry fetch errors during domain reload (used for `compile`)

## Integration runner

`tests/integration/runner.mjs` loads `scenarios/full-lifecycle.json`, attaches to an Editor instance, runs steps with per-step 20s cap.

Skip semantics: no heartbeat within 20s → stderr hints → exit `0`, `report.json` has `skipped: true`.

## Extending

New Unity commands only require `[CliCommand]` in the connector — no changes here unless CLI UX is needed.
