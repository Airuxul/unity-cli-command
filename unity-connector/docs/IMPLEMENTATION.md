# unity-connector — Implementation

Version: 0.1.7

## Package

- **UPM name:** `com.airuxul.unity-connector`
- **Version:** see `package.json` (synced with this doc `Version:` line for `doc:check`)

## Code organization (Core)

| Module | Files | Responsibility |
|--------|-------|----------------|
| Commands | `Commands/`, `CommandDiscovery`, `CommandPipeline` | Typed command interfaces, runtime-bound execution, host-aware routing |
| HTTP | `Http/`, `ConnectorJson` | `ConnectorHttpEndpoint`, `MainThreadHttpWork`, listener, routes, JSON bodies |
| Command state | `CommandStateCore`, `CommandContext`, `CommandNotifier`, `CommandPersistence` | Shared deferred lifecycle; Editor/Runtime managers |
| Execution | `CommandExecutor` | Shared command execution entry (single path) |
| Play hosts | `Runtime/Http/*`, `Runtime/State/*`, `Runtime/Bootstrap/*` | `editor_play` / `player` HTTP on main thread |

Editor assembly adds deferred command dispatch, completion policies, builtins, and services (see folder layout below).

### Editor assembly folders (`UnityCliConnector.Editor`)

| Folder | Contents |
|--------|----------|
| `Http/` | `EditorHttpSupervisor`, `EditorHttpLocalCache`, `EditorHttpHost`, `EditorMainThreadHttp`, `EditorHttpSession`, `EditorHttpHealth`, `EditorPlayHttpBootstrap` |
| `Dispatch/` | `EditorCommandHost`, `EditorCommandExecutor`, `EditorCommandStore`, `CommandStateManager` |
| `Commands/` | Builtin Editor commands (`play`, `compile`, `screenshot`, …) |
| `Completion/` | Deferred completion policies (`enter_play`, `exit_play`, `compilation`) |
| `Services/` | `EditorManageService`, `AssetRefreshService`, `UnityConsoleReader`, … |
| `State/` | `EditorStateProvider`, `EditorPlayState` |
| `Params/` | CLI parameter DTOs for Editor commands |
| `Infrastructure/` | `MainThread` |
| `Build/` | `ConnectorPlayerBuildGuard` |

### Runtime assembly folders (`UnityCliConnector.Runtime`)

| Folder | Contents |
|--------|----------|
| `Http/` | `PlayModeHttpEndpoint`, `PlayModeHttpHosts`, `PlayModeCommandBridge`, `PlayModeCommandHost`, `PlayModeCommandExecutor` |
| `Commands/` | Runtime builtins (`echo`, log helpers, tests) |
| `State/` | `RuntimeCommandStateManager`, `RuntimeCommandStore` |
| `Params/` | Runtime command parameter DTOs |
| `Bootstrap/` | `DevPlayerBootstrap` |
| `UI/` | `RuntimeCliConsole` (optional dev UI) |

## Assemblies

| Assembly | Platform | Contents |
|----------|----------|----------|
| `UnityCliConnector.Core` | all | Protocol, `CommandDiscovery`, `CommandExecutor`, `CommandStateCore`, `ConnectorJson` |
| `UnityCliConnector.Editor` | Editor | `EditorHttpHost` (Edit Mode), `EditorPlayHttpBootstrap` (Play → `EditorPlayHttpHost`), `CommandStateManager`, builtins |
| `UnityCliConnector.Runtime` | Editor + Development Build player | Play-mode HTTP host stack, runtime command store/state, runtime builtins, `DevPlayerBootstrap` |

## HTTP API (Editor)

| Method | Path | Response |
|--------|------|----------|
| GET | `/health` | `{ ok, host, connector_build, catalog_version, bind_mode }` |
| POST | `/list` | `{ ok, catalog_version, commands[], alias_to_command }` — each command includes `params[]` help lines |
| POST | `/command` | `200` sync or `202` + `command_id` |
| GET | `/commands/{id}` | `{ status, result?, error? }` |

| Host (`/health`) | When | Default port | Env override |
|------------------|------|--------------|--------------|
| `editor` | Editor Edit Mode | **6547** | `UNITY_CMD_PORT` |
| `editor_play` | Editor Play Mode | **6794** | `UNITY_CMD_EDITOR_PLAY_PORT` |
| `player` | Development Build player | **6795** | `UNITY_CMD_PLAYER_PORT` |

All three can run on one PC at the same time without port clashes. CLI: named profiles (`unity-cmd profile create`).

## Runtime / Play-mode stack

Play-mode HTTP reuses the same `ConnectorRequestDispatcher` and `HttpServer` as the Editor host. Runtime logic is now isolated in `UnityCliConnector.Runtime` (sibling to `Editor`).

### Components

| Type | Role |
|------|------|
| `ConnectorHttpEndpoint` (Core) | Shared listener: `TryStart` / `Stop` / `TryProbeHealth`; used by Editor supervisor and play-mode stacks |
| `MainThreadHttpWork` (Core) | Shared drain for `POST /command`, `POST /list`, `GET /commands/{id}` on main thread |
| `PlayModeHttpStack` | Wires `PlayModeCommandHost` + `PlayModeCommandBridge` + `ConnectorHttpEndpoint` for one port |
| `PlayModeHttpHosts.EditorPlayHttpHost` | `host=editor_play`, port `ResolveEditorPlayPort()` |
| `PlayModeHttpHosts.PlayerHttpHost` | `host=player`, port `ResolvePlayerPort()` |
| `PlayModeCommandHost` | `ICommandHost`; `/health` host string; routes via unified `CommandPipeline` |
| `PlayModeCommandBridge` | `ICommandScheduler`; queues POST `/command` on a hidden `GameObject` `Update` (main thread) |
| `EditorPlayHttpBootstrap` | `[InitializeOnLoad]` — start/stop `EditorPlayHttpHost` on Play Mode transitions |
| `DevPlayerBootstrap` | `[RuntimeInitializeOnLoadMethod]` — `PlayerHttpHost.Start()` in Development Build players |
| `ConnectorHttpLifecycle` | Shared start/stop for Editor and play-mode listeners |
| `EchoRuntimeCommand` | Builtin `echo`, `Scope = Runtime` — smoke test for play/player hosts |

### Lifecycle

**Editor HTTP (`editor`, :6547):**

1. `EditorHttpSupervisor` owns the listener: start/stop, health probe, watchdog, catalog warmup.
2. `EditorHttpLocalCache` persists `~/.unity-cmd/editor-http.json` (`pid`, `session_id`, `listener_id`, `status`) so each domain reload can release orphans and only one server binds the port.
3. Stays up during Play; `beforeAssemblyReload` stops cleanly and marks cache `stopped`; `afterAssemblyReload` / Play / watchdog call `RequestEnsureRunning`.
4. `GET /health` returns `session_id`, `generation`, `ready`, `blocking_reasons`.
5. `POST /list`, `POST /command`, `GET /commands/{id}` run on the Editor main thread via `EditorMainThreadHttp`.

**Editor Play (`editor_play`):**

1. `EditorApplication` enters Play → `EditorPlayHttpBootstrap` calls `EditorPlayHttpHost.Start()`.
2. `PlayModeHttpStack` starts `ConnectorHttpEndpoint` and `PlayModeCommandBridge.EnsureStarted()`.
3. Exiting Play → `EditorPlayHttpHost.Stop()`; listener thread shuts down without logging benign abort errors (`HttpServer` treats `ThreadAbortException` / listener stop as normal).

**Dev player (`player`):**

1. After first scene load, `DevPlayerBootstrap` starts `PlayerHttpHost` (same endpoint stack, different `host` on `/health`).
2. Release builds: `UnityCliConnector.Runtime.asmdef` excludes player runtime bootstrap by define constraint (`DEVELOPMENT_BUILD`) — no port 6795 listener.

### Scope routing on play-mode hosts

```text
ConnectorHostKind.IsPlayModeHost(hostKind)  →  editor_play | player
```

| Handler `CommandScope` | `editor` :6547 | `editor_play` :6794 | `player` :6795 |
|------------------------|----------------|---------------------|----------------|
| `Editor` | Yes | No | No |
| `Runtime` | No | Yes | Yes |
| `Any` | Yes | Yes | Yes (if shipped in build) |

Profiler, screenshot, and `state` are **Editor** scope — use profile **`editor`** (:6547), including during Play Mode.

### Play-mode HTTP

- **Deferred commands supported:** `POST /command` may return `202`; `GET /commands/{id}` via `RuntimeCommandStore`.
- **Main thread:** Handlers run on the play loop via `BridgeDriver.Update`; avoid blocking.
- **Screenshot in Play:** `screenshot` (`Scope = editor`) — use profile **`editor`** (:6547) even while playing; `view=game` captures Game view; `view=scene` needs Scene view.
- **Profiler in Play:** Use `editor` profile on :6547 for edit-mode profiler; in-play hierarchy often uses `editor-play` if command is `runtime`/`any` scope only.

### CLI examples (Runtime)

```bash
# Editor Play — dedicated endpoint (recommended during Play)
unity-cmd --profile editor-play ping
unity-cmd --profile editor-play echo
unity-cmd --profile editor screenshot --view game --output_path Screenshots/play.png

# Development Build player
unity-cmd --profile package-play ping
unity-cmd --profile package-play echo

# Enter/exit Play still use Editor host
unity-cmd --profile editor play
unity-cmd --profile editor stop
```


### POST /command body

```json
{
  "command": "compile",
  "parameters": { },
  "request_id": "optional-uuid"
}
```

Sync success: `200` + `{ ok: true, data: {...} }`.  
Command accepted: `202` + `{ command_id, request_id }`.

## Command discovery

- Command class with `ICommandDescriptorProvider.Descriptor` (typically `new CommandDescriptor<TParams>(...)` or `new DeferredCommandDescriptor<TParams>(...)`).
- Execution entry is unified as `Run(...)`; runtime callbacks are injected through `CommandBase`.
- `CommandDiscovery` scans loaded assemblies at domain load; it builds handlers from command interfaces and instantiates command objects per execution.
- Param type: `Descriptor.ParamType` or inferred from `Run(...)` parameter list.
- `CommandCompletionCatalog.GetCompletionKind`: deferred commands use `DeferredCommandDescriptor.Completion`; override `refresh` + `compile=true` → `compilation`.

## Command parameters (`CliParam`)

| Component | Role |
|-----------|------|
| `CliParamAttribute` | CLI flag metadata: optional `key` (default: camelCase property name), `Required`, `Description`, `AllowedValues`, `AlternateKeys` |
| `CliParamBinder` | Normalizes incoming `Dictionary<string, object>` → JSON → `DeserializeObject<T>` (Newtonsoft) |
| `CliParamContractResolver` | Maps `[CliParam]` keys to JSON property names |
| `ICommandHandler.ParamDescriptions` | Help lines for `POST /list` / CLI `help` |

Builtin param classes: `Editor/Params/EditorCommandParams.cs`, `Runtime/Params/EchoParams.cs`.

**Validation:** Missing `Required` properties throw bind errors before `Run` is invoked.

**Legacy input names:** `AlternateKeys` on a property (e.g. `wait` accepts `wait_for_completion`, `lines` accepts `count`).

**No `CliParams` bag:** Commands receive a typed instance only; services take `*Params` or primitives directly.

## Deferred lifecycle

**Editor host (`CommandStateManager`):**

1. `CommandPipeline` creates command status via `CommandStateManager`.
2. `compile` / `refresh --compile` call `ScriptCompilationService` (Unity pipeline events; see below); play/stop run immediately.
3. `EditorApplication.update` → completion policy tick (fallback when compilation service did not finish).
4. Terminal: `succeeded` | `failed` | `orphaned` (no progress 20s).
5. **No handler redispatch** after domain reload.

**Play / player hosts (`RuntimeCommandStateManager`):**

1. `PlayModeCommandHost` → `CommandPipeline` → `RuntimeCommandStateManager.Create(host, …)`.
2. `PlayModeCommandExecutor.ExecuteCommand` runs the command on the play main thread.
3. `PlayModeCommandBridge.Drain` ticks `RuntimeCommandStateManager` each frame; command status persists per host in `PlayerPrefs`.
4. `GET /commands/{id}` via `RuntimeCommandStore` + shared `CommandResponseBuilder`.

### Completion policies

| Kind | Host | Completes when |
|------|------|----------------|
| `compilation` | Editor | `ScriptCompilationService`: `compilationStarted`/`compilationFinished` (matched `context`); if no cycle starts, idle frames + `!isCompiling`; policy fallback: `!EditorApplication.isCompiling`. No public API to pre-check if compile is needed (Unity 2021.3+). |
| `enter_play` | Editor | `EditorApplication.isPlaying` |
| `exit_play` | Editor | `!EditorApplication.isPlaying` |
| `started` (`signal`) | Editor / Play / player | command decides completion via `ctx.CompleteSuccess/CompleteFail` or `ctx.RunBackground` (no extra policy class needed) |

## Host routing (`CommandAvailability`)

| HTTP host | Allowed scopes |
|-----------|------------------|
| `editor` (:6547) | `Editor`, `Any` |
| `editor_play` (:6794) | `Runtime`, `Any` |
| `player` (:6795) | `Runtime`, `Any` |

CLI mirrors this in `checkScopeCompatibility` before `POST /command`.

## Instance discovery

CLI (`unity-cmd/src/client/connection.js`): profile file only — no port scanning.

1. `~/.unity-cmd/profiles/{name}.json` from `unity-cmd profile create`
2. `--profile` or `UNITY_CMD_PROFILE`
3. `GET /health` must match `connector_host` in the profile

**LAN:** `UNITY_CMD_BIND=lan` or `UNITY_CMD_LAN=1` on Unity; remote machine: `unity-cmd profile create phone --host <ip> --port <p> --host-kind editor_play`.

**Compile / domain reload:** Editor HTTP stops and restarts (including brief gaps on enter/exit Play); CLI `allow_connection_retry` on deferred commands retries until timeout. In-flight command status is lost (poll returns `command_not_found`); re-issue command after reload.

**HTTP (unified):** `ConnectorRequestDispatcher` — `/health`, `POST /list`, `GET /commands/{id}`, `POST /command` via `IMainThreadHttpScheduler` (Editor / play-mode bridges).

## Built-in commands

| Command | Aliases | Deferred | Scope | Notes |
|---------|---------|----------|-------|-------|
| `ping` | — | no | any | Health |
| `state` | — | no | editor | Snapshot + `connector_build`, `recent_console` |
| `echo` | — | no | editor / runtime | Editor vs play handlers; shared `EchoParams` |
| `compile` | `recompile`, `reload` | yes | editor | `compilation`, 30s default |
| `play` | — | yes | editor | `enter_play`, 60s |
| `stop` | — | yes | editor | `exit_play`, 60s |
| `refresh` | — | if `compile=true` | editor | `AssetRefreshService.Refresh(RefreshParams)` |
| `console` | — | no | editor | LogEntries reflection |
| `menu` | — | no | editor | `ExecuteMenuItem` |
| `screenshot` | — | no | editor | Scene/Game PNG; Editor host only |
| `exec` | — | no | editor | Roslyn/csc snippet |
| `profiler` | — | no | editor | Editor host; works during Play on :6547 |
| `manage` | — | no | editor | play/stop/pause/refresh/tags/layers/tools |
| `reserialize` | — | no | editor | ForceReserializeAssets |

## Command parameters (agent reference)

### `state`

No parameters. Returns `is_playing`, `is_compiling`, `ready_for_tools`, `blocking_reasons`, `active_command`, `connector_build`, `recent_console` (last N entries).

### `console`

| Param | Default | Values |
|-------|---------|--------|
| `type` | `error,warning` | `error`, `warning`, `log` (comma-separated) |
| `lines` / `count` | all matched | max entries |
| `stacktrace` | `user` | `none`, `user`, `all` |
| `clear` | false | `true` clears console |

### `menu`

| Param | Required |
|-------|----------|
| `menu_path` | yes | e.g. `File/Save Project` |

### `screenshot`

`Scope = any` (Editor host in edit and Play). During Play, `view=game` uses `Camera.main` render, or `ScreenCapture.CaptureScreenshotAsTexture` fallback.

| Param | Notes |
|-------|-------|
| `view` | `game`, `scene` (`scene` needs active SceneView; may fail in Play) |
| `output_path` | relative to project or absolute |
| `width` / `height` | default 1920×1080 |

### `exec`

| Param | Notes |
|-------|-------|
| `code` | C# snippet; use `return` for value |
| `usings` | extra namespaces, comma-separated |
| `csc` / `dotnet` | optional compiler paths |

### `profiler`

| Param | Notes |
|-------|-------|
| `action` | `hierarchy` (default), `enable`, `disable`, `status`, `clear` |
| `frame` | single frame index (default: last) |
| `frames` | average last N frames — **known bug: may throw cast error** |
| `from`, `to` | inclusive range for average — same bug risk |
| `thread` | default `0` |
| `root`, `parent` | subtree filter |
| `min` | min ms threshold |
| `sort` | `total`, `self`, `calls` |
| `max` | max items (default 30) |
| `depth` | tree depth (default 1; `0` or large → deep) |

**Agent workflow (Edit Mode host):** `enable` on `:6547` → `play` → wait → `stop` → `hierarchy --frame <n>` on `:6547`.

**In Play (`editor_play`):** `profiler --action status` / `hierarchy` and `screenshot --view game` on `:6794` without stopping Play.

### `manage`

| Param | Notes |
|-------|-------|
| `action` | `play`, `stop`, `pause`, `refresh`, `set_active_tool`, `add_tag`, `remove_tag`, `add_layer`, `remove_layer` |
| `wait` | bool, block until play transition done (`wait_for_completion` accepted via `AlternateKeys`) |
| `tool_name`, `tag_name`, `layer_name` | per action |

### `reserialize`

| Param | Notes |
|-------|-------|
| `paths` | comma-separated (`path` accepted) |
| omit | entire project (slow) |

### `refresh`

| Param | Notes |
|-------|-------|
| `compile` | `true` → triggers compilation deferred flow |
| `force` | `true` → allow refresh while entering play mode |

## Main-thread execution

Editor commands run on the main thread via `EditorMainThreadHttp` (queued from HTTP); worker threads never call Unity APIs or `CommandDiscovery`.

## Services (Editor)

| Service | Used by |
|---------|---------|
| `UnityConsoleReader` | `console` |
| `AssetRefreshService` | `refresh` |
| `ProfilerHierarchyService` | `profiler` |
| `CsharpExecutor` | `exec` |

## Tests (EditMode)

Assembly: `UnityCliConnector.Tests.Editor` in consuming project.

```bash
Unity -runTests -batchmode -projectPath <Project> -testPlatform editmode \
  -assemblyNames UnityCliConnector.Tests.Editor
```

Set `UNITY_PROJECT_PATH` for external test project reference.

## Extending

```csharp
using UnityCliConnector.Commands;

public sealed class MyToolParams
{
    [CliParam(Required = true, Description = "example flag")]
    public string Name { get; set; }
}

public class MyToolCommand : CommandBase, ICommand<MyToolParams>, ICommandDescriptorProvider
{
    public CommandDescriptor Descriptor { get; } = new DeferredCommandDescriptor<MyToolParams>(
        "my.tool", CommandScope.Editor, "…",
        defaultTimeoutMs: 30000);

    public void Run(MyToolParams p)
    {
        // kick off async work
        MarkRunning();
    }
}
```

### Task-based deferred template (recommended)

For most async commands, use default `started` completion and report from `CommandContext` directly (no custom completion policy class).

```csharp
using System.Threading;
using UnityCliConnector.Commands;

public sealed class SlowHashParams
{
    [CliParam(Required = true, Description = "input text")]
    public string Input { get; set; }
}

public sealed class SlowHashCommand : CommandBase, ICommand<SlowHashParams>, ICommandDescriptorProvider
{
    public CommandDescriptor Descriptor { get; } = new DeferredCommandDescriptor<SlowHashParams>(
        "slow.hash",
        CommandScope.Runtime,
        "Compute hash in background Task",
        defaultTimeoutMs: 15000);

    public void Run(SlowHashParams p)
    {
        MarkRunning();
        _ = System.Threading.Tasks.Task.Run(() =>
        {
            Thread.Sleep(800); // simulate expensive work
            var hash = p.Input?.GetHashCode() ?? 0;
            CompleteSuccess(new { hash, input = p.Input });
        });
    }
}
```

If you need main-thread Unity API after background work, compute in a Task first, then marshal the apply step back to your main-thread bridge/update loop and call `CompleteSuccess(...)` there.

1. Add name to `CommandNames.cs` (recommended).
2. Increment `ConnectorBuild.Id`.
3. `unity-cmd --profile editor compile` — command and `params` appear on next `POST /list` / `help`.

See [../../docs/MAINTENANCE.md](../../docs/MAINTENANCE.md) for immediate vs deferred patterns.
