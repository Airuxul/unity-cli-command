# unity-connector — Implementation

Version: 0.1.0

## Assemblies

| Assembly | Platform | Contents |
|----------|----------|----------|
| `UnityCliConnector.Core` | all | Protocol, discovery, router, SimpleJson |
| `UnityCliConnector.Editor` | Editor | HTTP host, jobs, heartbeat, builtins |
| `UnityCliConnector.Runtime` | Player | Runtime HTTP (debug builds), `echo.runtime` |

## HTTP API

| Method | Path | Response |
|--------|------|----------|
| GET | `/health` | `{ ok, host }` |
| POST | `/command` | `200` sync result or `202` + `job_id` |
| GET | `/jobs/{id}` | job status + result |
| POST | `/list` | command catalog |

### POST /command body

```json
{
  "command": "compile",
  "parameters": { "compile": true },
  "request_id": "optional"
}
```

## Job lifecycle

1. `CommandJobCatalog` classifies destructive commands.
2. `JobManager.Create` stores job in `SessionState` key `UnityCliConnector.Jobs`.
3. Side effect runs on `EditorApplication.delayCall` (compile, play, stop).
4. `EditorApplication.update` → `CompletionPolicy.Tick()` observes `isCompiling` / `isPlaying`.
5. **No handler redispatch** after domain reload.

Orphan: no progress for 20s → `orphaned`.

## Completion policies

| Kind | Completes when |
|------|----------------|
| `compilation` | `!EditorApplication.isCompiling` after running |
| `enter_play` | `EditorApplication.isPlaying` |
| `exit_play` | `!EditorApplication.isPlaying` |

## Heartbeat

Every 0.5s writes `~/.unity-cmd/instances/{hash}.json`:

```json
{
  "project_path": "C:/Game",
  "host": "127.0.0.1",
  "port": 6400,
  "protocol_version": 1,
  "editor_state": {
    "is_playing": false,
    "is_compiling": false,
    "ready_for_tools": true,
    "blocking_reasons": [],
    "active_job": null
  }
}
```

Port: `UNITY_CMD_PORT` or `6400 + hash(dataPath) % 800`.

## Command routing

- Editor host, not playing: Editor + Any commands.
- Editor host, playing: Runtime + Any commands; Editor-scoped commands rejected.
- Runtime host (debug player): Runtime + Any only.

## Main-thread execution

Sync commands use `EditorCommandExecutor.ExecuteSync` with `delayCall` + wait when invoked from HTTP thread.
