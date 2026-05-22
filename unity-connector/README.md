# unity-connector

Unity Editor / Player HTTP bridge for [unity-cmd](../unity-cmd/).

**Version:** 0.1.0  
**UPM name:** `com.airuxul.unity-connector`

## Install (local path)

In your Unity project's `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.airuxul.unity-connector": "file:/path/to/unity-cli/unity-connector"
  }
}
```

Open the project in the Editor. The connector logs a URL like `http://127.0.0.1:6400/` and writes a heartbeat under `~/.unity-cmd/instances/`.

## Built-in commands

| Command | Scope | Job |
|---------|-------|-----|
| `ping` | any | no |
| `connector.state` | editor | no |
| `echo.editor` | editor | no |
| `echo.runtime` | runtime (Play Mode) | no |
| `compile` | editor | yes |
| `editor.recompile` | editor | yes (same as `compile`) |
| `editor.play` | editor | yes |
| `editor.stop` | editor | yes |
| `refresh` | editor | yes if `compile=true` |

## Extend with `[CliCommand]`

```csharp
using UnityCliConnector;

public static class MyTool
{
    [CliCommand("my.tool", Scope = CommandScope.Editor, Description = "Example")]
    public static CommandResult Run(CliParams p)
    {
        return CommandResult.Success(new { done = true });
    }
}
```

Then: `unity-cmd my.tool`

Recompile after changing the connector (from repo):

```bash
unity-cmd recompile
# or: unity-cmd compile / unity-cmd editor.recompile
```

## EditMode tests (optional)

From a machine with Unity installed, pointing at **your** project:

```bash
Unity -runTests -batchmode -projectPath <YourProject> -testPlatform editmode \
  -assemblyNames UnityCliConnector.Tests.Editor
```

Set `UNITY_PROJECT_PATH` to the same path for convenience.

See [docs/IMPLEMENTATION.md](docs/IMPLEMENTATION.md).
