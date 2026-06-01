using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityCliConnector
{
    [InitializeOnLoad]
    public static class CommandStateManager
    {
        private const string SessionKey = "UnityCliConnector.CommandStates";
        private static readonly Dictionary<string, ICompletionPolicy> Policies = new()
        {
            { CommandCompletionCatalog.CompletionCompilation, new Completion.CompilationPolicy() },
            { CommandCompletionCatalog.CompletionEnterPlay, new Completion.EnterPlayModePolicy() },
            { CommandCompletionCatalog.CompletionExitPlay, new Completion.ExitPlayModePolicy() },
        };
        private static Dictionary<string, CommandRecord> _commands = new();

        static CommandStateManager()
        {
            Load();
            EditorApplication.update += Tick;
        }

        public static IReadOnlyDictionary<string, CommandRecord> AllCommands => _commands;
        public static CommandRecord Get(string id) => id != null && _commands.TryGetValue(id, out var command) ? command : null;
        public static CommandRecord Create(string command, string completionKind, string requestId) =>
            CommandStateCore.Create(_commands, command, completionKind, requestId, CommandStateCore.UtcNowMs, Save);

        public static void Fail(string id, string error)
        {
            if (!_commands.TryGetValue(id, out var command)) return;
            CommandStateCore.MarkFailed(command, error, CommandStateCore.UtcNowMs(), Save);
        }

        public static void MarkRunning(string id)
        {
            if (!_commands.TryGetValue(id, out var command) || CommandStateCore.ShouldSkip(command)) return;
            command.Status = CommandStatus.Running;
            command.UpdatedAtUtcMs = CommandStateCore.UtcNowMs();
            Save();
        }

        public static void Succeed(string id, object result)
        {
            if (!_commands.TryGetValue(id, out var command)) return;
            CommandStateCore.MarkSucceeded(command, result, CommandStateCore.UtcNowMs(), true, Save);
        }

        private static void Tick()
        {
            if (_commands.Count == 0) return;
            var state = EditorStateProvider.Capture();
            CommandStateCore.TickCommands(
                _commands.Values,
                Save,
                command => ResolveTryComplete(command, state),
                command => $"No completion policy for '{command.CompletionKind}'.",
                command => string.IsNullOrEmpty(command.CompletionKind) || string.Equals(command.CompletionKind, CommandCompletionCatalog.CompletionDeferred, StringComparison.Ordinal),
                serializeResultJson: false);
        }

        private static CommandStateCore.TryCompleteCommand ResolveTryComplete(CommandRecord command, EditorStateSnapshot state)
        {
            if (!Policies.TryGetValue(command.CompletionKind ?? "", out var policy)) return null;
            return (CommandRecord current, out object result, out string error) => policy.TryComplete(current, state, out result, out error);
        }

        private static void Load()
        {
            var json = SessionState.GetString(SessionKey, "");
            if (string.IsNullOrEmpty(json))
            {
                _commands = new Dictionary<string, CommandRecord>();
                return;
            }

            try
            {
                var wrapper = JsonUtility.FromJson<CommandListWrapper>(json);
                _commands = new Dictionary<string, CommandRecord>();
                if (wrapper?.Items == null) return;
                foreach (var item in wrapper.Items)
                {
                    if (!string.IsNullOrEmpty(item?.Id)) _commands[item.Id] = item;
                }
            }
            catch
            {
                _commands = new Dictionary<string, CommandRecord>();
            }
        }

        private static void Save()
        {
            if (!MainThread.IsCurrent)
            {
                Debug.LogWarning("[unity-connector] CommandStateManager.Save skipped (not main thread).");
                return;
            }

            var wrapper = new CommandListWrapper { Items = _commands.Values.ToList() };
            SessionState.SetString(SessionKey, JsonUtility.ToJson(wrapper));
        }
    }
}
