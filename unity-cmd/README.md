# unity-cmd

Node.js CLI that sends commands to [unity-connector](../unity-connector/) over HTTP.

**Version:** 0.1.0

## Install

```bash
cd unity-cmd
npm install
npm link   # optional
```

## Usage

```bash
unity-cmd ping
unity-cmd list
unity-cmd editor.play
unity-cmd echo.editor --message hello
unity-cmd recompile          # recommended after editing unity-connector (120s job timeout)
unity-cmd compile            # same as recompile
unity-cmd editor.recompile   # connector-native alias
```

## Environment

| Variable | Description |
|----------|-------------|
| `UNITY_CMD_PROJECT` | Select instance by project path or folder name |
| `UNITY_CMD_HOST` | Override host |
| `UNITY_CMD_PORT` | Override port |
| `UNITY_CMD_TIMEOUT_MS` | Default timeout (20000) |

## npm scripts

| Script | Description |
|--------|-------------|
| `npm run verify` | Unit tests + documentation version check |
| `npm run test:unit` | Node unit tests only |
| `npm run test:integration` | Full lifecycle against an open Editor (skips if none) |
| `npm run doc:check` | Sync doc `Version:` headers with package.json |

## Integration tests

1. Install `unity-connector` in your Unity project and open it in the Editor.
2. Optionally set `UNITY_CMD_PROJECT` to your project path.
3. Run `npm run test:integration`.

If no instance is found within 20 seconds, the runner logs hints and exits `0` (skipped).

See [docs/IMPLEMENTATION.md](docs/IMPLEMENTATION.md) for protocol details.
