using System.Collections.Generic;
using UnityCliConnector.Editor.Services;
using UnityEditor;

namespace UnityCliConnector
{
    public static class EditorStateProvider
    {
        public static EditorStateSnapshot Capture()
        {
            var compiling = EditorApplication.isCompiling;
            var playing = EditorApplication.isPlaying;
            return new EditorStateSnapshot
            {
                IsCompiling = compiling,
                IsPlaying = playing,
                ReadyForTools = !compiling,
            };
        }

        public static Dictionary<string, object> ToManifestObject()
        {
            var state = Capture();
            var activeCommand = FindActiveCommandId();
            var manifest = new Dictionary<string, object>
            {
                ["is_playing"] = state.IsPlaying,
                ["is_compiling"] = state.IsCompiling,
                ["ready_for_tools"] = state.ReadyForTools,
                ["blocking_reasons"] = BuildBlockingReasons(state),
                ["active_command"] = activeCommand,
                ["connector_build"] = ConnectorBuild.Id,
            };

            if (UnityConsoleReader.IsReady)
            {
                var entries = UnityConsoleReader.Read(new UnityConsoleReader.ConsoleReadOptions
                {
                    TypeFilter = "error,warning",
                    MaxEntries = 5,
                    Stacktrace = "none",
                });
                manifest["recent_console"] = entries;
                manifest["recent_console_count"] = entries.Count;
            }
            else
            {
                manifest["recent_console"] = new List<Dictionary<string, object>>();
                manifest["recent_console_count"] = 0;
            }

            return manifest;
        }

        private static string FindActiveCommandId()
        {
            foreach (var pair in CommandStateManager.AllCommands)
            {
                var command = pair.Value;
                if (command.Status is CommandStatus.Pending or CommandStatus.Running)
                    return command.Id;
            }

            return null;
        }

        internal static string[] BuildBlockingReasonsPublic(EditorStateSnapshot state) =>
            BuildBlockingReasons(state);

        private static string[] BuildBlockingReasons(EditorStateSnapshot state)
        {
            if (state.IsCompiling)
                return new[] { "compiling" };
            if (state.IsPlaying)
                return new[] { "playing" };
            return System.Array.Empty<string>();
        }
    }
}
