# unity-cmd — Implementation

Version: 0.1.0

## Role

Thin HTTP client and argv front-end. No Unity business logic; all commands come from the connector catalog except local meta commands.

## Entry point

`bin/unity-cmd.js` → `src/dispatch.js` with `process.argv` slice from index 2. No command prints local help.

## Modules

| Path | Responsibility |
|------|----------------|
| `src/timeout.js` | Default 20s; `UNITY_CMD_TIMEOUT_MS` |
| `src/client/connection.js` | Profiles, `PING_MAX_ATTEMPTS` (3) / `PING_RETRY_INTERVAL_MS` (3000), `resolveTarget`, `waitForInstance` |
| `src/client/http.js` | `fetch` + `AbortSignal` timeout |
| `src/client/command-status.js` | Poll `GET /commands/{id}` until terminal status |
| `src/client/command.js` | `ping` → `GET /health`; `fetchCatalog` → `POST /list`; `sendCommand` → `POST /command` |
| `src/profile.js` | `profile list|show|create|set|delete` |
| `src/catalog.js` | Cache, alias resolve, scope checks, `connector_build` + `catalog_version` invalidation |
| `src/params.js` | Coerce flags (`compile`, `clear`, `force`) for Unity JSON bodies |
| `src/errors.js` | `error_code`, `hint` on failures |
| `src/help.js` | Local / offline / live help (parameters from catalog) |
| `src/dispatch.js` | Route meta + remote commands |

## Local meta commands

| Command | Behavior |
|---------|----------|
| `help` | No profile → local usage; with profile → live catalog help (**always refetches** when online) |
| `profile` | Manage `~/.unity-cmd/profiles/*.json` |
| `ping` | `GET /health` (requires `--profile`) |
| `list` | `loadCatalog` JSON (requires `--profile`) |

Remote commands require `--profile <name>` or `UNITY_CMD_PROFILE`.

### `list` output schema

```json
{
  "ok": true,
  "profile": "editor",
  "catalog_version": "4a6528daab10",
  "connector_build": 17,
  "commands": [
    {
      "name": "console",
      "scope": "editor",
      "params": ["--type <string> [error|warning|log] ...", "--lines <int> max entries", "..."]
    }
  ],
  "alias_to_command": { "recompile": "compile" }
}
```

Flag: `--refresh-catalog` forces network fetch and rewrites cache file.

## Profiles

- Path: `~/.unity-cmd/profiles/{name}.json`
- Fields: `name`, `host`, `port`, `connector_host` (`editor` | `editor_play` | `player`), `updated_at`
- `profile create` pings `/health` by default (`--no-verify` to skip)
- `resolveTarget({ profile })` loads file, verifies `/health` `host` matches `connector_host`

Default ports: editor **6547**, editor_play **6794**, player **6795**.

Integration mapping (`PROFILE_BY_HOST_KIND`): `editor_play` → profile `editor-play`, `player` → `package-play`.

## Catalog cache

- Path: `~/.unity-cmd/cache/catalog-<host>:<port>.json` (host `:` → `_` in filename)
- Fields on disk: `updated_at`, `expires_at` (`updated_at` + 1 day), `host_kind`, `catalog_version`, `connector_build`, `commands`, `alias_to_command`
- Valid when: file exists, **not** `isCatalogExpired`, `ping` succeeds, `connector_build` / `catalog_version` match `/health`, `host_kind` matches profile
- `list` JSON includes `cache_path`, `updated_at`, `expires_at`, `catalog_expired`, `catalog_ttl_ms` for agents
- Aliases and `params` from Unity `POST /list` only
- `readCachedCatalog(target)` — shared disk read for offline help fallback

## Scope check (CLI)

`checkScopeCompatibility` mirrors Unity `CommandAvailability`:

- Play hosts (`editor_play`, `player`): `runtime` + `any` only
- Editor host: `editor` + `any` only

## `resolveRemoteCommand`

1. Map argv command through `alias_to_command`
2. Look up `commands_by_name[canonical]`
3. `refresh` + `compile=true` → extend timeout to compile default
4. Return `{ command, allowConnectionRetry, minTimeoutMs }`

## Remote command dispatch

1. `requireProfile` → `resolveTarget`
2. `loadCatalog` → `resolveRemoteCommand` → scope check
3. `POST /command`; `202` → `pollCommandStatus`
4. `printJson`; exit `0` / `1`

## Help formatting

`formatProfileHelp` prints each command, then indented lines from `entry.params` (generated on the Unity side from `[CliParam]` metadata).

When the connector is online, `runHelp` calls `loadCatalog(..., { forceRefresh: true })` so parameter lines are never served from a stale cache file.

## Agent cheat sheet

| Intent | Profile | Port |
|--------|---------|------|
| Compile, console, play/stop, profiler (edit) | `editor` | 6547 |
| Runtime in Editor Play | `editor-play` | 6794 |
| Dev Player build | `package-play` | 6795 |

`play` / `stop` always use profile **`editor`**.

## Integration runner

| Scenario | `UNITY_CMD_PROFILE` | File |
|----------|---------------------|------|
| `editor-lifecycle` (default) | `editor` | `scenarios/editor-lifecycle.json` |
| `player-runtime` | `package-play` | `scenarios/player-runtime.json` |

Steps may set `"hostKind": "editor_play"` to select profile via `profileNameForHostKind`.  
Step types: `sleepMs`, `waitProfile`, `assertFile`, `expectFailure`, `expectCatalog`, `expectConnectorBuild` (see `MIN_CONNECTOR_BUILD` in `src/constants.js`).

## Tests

`npm run test:unit` — no Unity required.

`npm run test:integration` — needs profiles + running connector; skips if unreachable.
