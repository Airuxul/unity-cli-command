
# Agent guide — unity-cli monorepo
**Start here for AI agents:** [unity-cmd-skill/SKILL.md](unity-cmd-skill/SKILL.md) → [guide.zh-CN.md](unity-cmd-skill/references/guide.zh-CN.md) (install: copy to `.cursor/skills/unity-cmd/` — see [README](../README.md#ai--agent-usage)). This file is a short reference only.

**Skill:** [unity-cmd-skill/SKILL.md](unity-cmd-skill/SKILL.md) → [guide.zh-CN.md](unity-cmd-skill/references/guide.zh-CN.md). **Catalog:** `list` only; stale if `catalog_expired` or `now >= expires_at`. No skill markdown cache.

## Doc map

| Document | Use when |
|----------|----------|
| [unity-cmd-skill/SKILL.md](unity-cmd-skill/SKILL.md) | **Primary** — Cursor Skill source + 3-step cache workflow |
| [ARCHITECTURE.md](ARCHITECTURE.md) | System design, request flow, assemblies, extension rules |
| [../unity-cmd/docs/IMPLEMENTATION.md](../unity-cmd/docs/IMPLEMENTATION.md) | CLI modules, profiles, catalog cache, errors, polling |
| [../unity-connector/docs/IMPLEMENTATION.md](../unity-connector/docs/IMPLEMENTATION.md) | HTTP API, deferred commands, parameters, play-mode rules |
| [MAINTENANCE.md](MAINTENANCE.md) | Command patterns, build bumps, extending connector |

## Quick facts

- **Monorepo:** `unity-cmd` (Node CLI) + `unity-connector` (Unity UPM `com.airuxul.unity-connector`).
- **No root `package.json`:** run npm scripts from `unity-cmd/`.
- **Command source of truth:** Unity `POST /list` → cached at `~/.unity-cmd/cache/catalog-<host>:<port>.json`.
- **Endpoints:** saved profiles in `~/.unity-cmd/profiles/*.json`; use `--profile` or `UNITY_CMD_PROFILE`. Verified with `GET /health`.
- **Local CLI-only:** `help`, `profile` (no profile). Remote: `ping`, `list`, connector commands (profile required).
- **After connector C# changes:** `unity-cmd --profile editor compile` (or `recompile`) or `refresh --compile true --timeout 30000`.
- **Help / params:** `unity-cmd --profile editor help` refetches catalog and prints `commands[].params` under each command.
- **Three endpoints (same PC):** `editor` :6547, `editor_play` :6794, `player` :6795. Port overrides are **Unity-side env vars**; CLI uses profile files.

## Profiles (copy-paste)

```bat
unity-cmd profile create editor --host 127.0.0.1 --port 6547 --host-kind editor
unity-cmd profile create editor-play --port 6794 --host-kind editor_play
unity-cmd profile create package-play --port 6795 --host-kind player
```

| Instance | Profile | Example commands |
|----------|---------|------------------|
| Editor | `editor` | `ping` · `echo` · `compile` · `play` · `profiler --action enable` |
| Editor Play | `editor-play` | `ping` · `echo` (runtime channel) |
| Dev player | `package-play` | `ping` · `echo` (runtime channel) |

Full lists: [../README.md#commands-per-instance](../README.md#commands-per-instance).

## Typical workflow

```text
1. unity-cmd --profile editor ping
2. unity-cmd --profile editor list [--refresh-catalog]
3. unity-cmd --profile <name> <command> [--flags]
4. If 202: CLI polls GET /commands/{id} for command status
5. On failure: read error_code + hint in JSON stdout
```

## Scope routing (by host)

| Profile / host | `editor` scope | `runtime` scope |
|----------------|----------------|-----------------|
| `editor` (:6547) | Yes | No → use `editor-play` for runtime `echo` |
| `editor-play` (:6794) | No | Yes |
| `package-play` (:6795) | No | Yes |

`CommandScope.Runtime` on the command `Descriptor` is the **scope**, not a profile name.

Same command name (`echo`) can exist twice (Editor + Runtime handlers); routing uses host + scope, not separate CLI names.

### Recommended Play flow

```text
1. --profile editor     → profiler --action enable
2. --profile editor     → play (deferred poll)
3. --profile editor-play → echo
4. --profile editor → profiler status/hierarchy, screenshot --view game (still :6547 while playing)
5. --profile editor → stop
```

Deferred command status (`202` + `GET /commands/{id}`) works on **all three hosts**. Editor uses built-in completion policies (`compile`, `play`, `stop`). Play/player use `started` completion — command reports via `CompleteSuccess/CompleteFail` (see connector IMPLEMENTATION).

## Structured failures (CLI)

| `error_code` | Meaning |
|--------------|---------|
| `NO_PROFILE` | Missing `--profile` / `UNITY_CMD_PROFILE` |
| `NO_INSTANCE` | Profile unreachable or `/health` host mismatch |
| `SCOPE_MISMATCH` | Command scope wrong for profile host (catalog entry must exist) |
| `CONNECTION_FAILED` | HTTP unreachable |
| `CATALOG_FETCH_FAILED` | `POST /list` failed |
| `DEFERRED_COMMAND_FAILED` / `COMMAND_STATUS_TIMEOUT` | Deferred command failed or timed out |

## Do not

- Hardcode command tables in `unity-cmd`.
- Call `compile` / `play` / `stop` on `editor-play` profile (use `editor`).
- Expect runtime-channel `echo` on `editor` profile — use `editor-play` or `package-play` for `channel=runtime`.

## Tests (from `unity-cmd/`)

| Script | Unity required |
|--------|----------------|
| `npm run verify` | No |
| `npm run test:integration` | Yes (skips if unreachable) |

Set `UNITY_CMD_PROFILE=editor` or `package-play` per scenario.
