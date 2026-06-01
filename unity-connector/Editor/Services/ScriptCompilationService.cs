using System;
using System.Collections.Generic;
using UnityCliConnector.Commands;
using UnityEditor;
using UnityEditor.Compilation;

namespace UnityCliConnector.Editor.Services
{
    /// <summary>
    /// Requests script compilation and completes via <see cref="CompilationPipeline"/>
    /// events available in Unity 2021.3+ (plugin minimum).
    /// Unity does not expose a pre-check for whether compilation is needed; see
    /// <see cref="RequestWithCompletion"/> remarks.
    /// </summary>
    public static class ScriptCompilationService
    {
        private const int IdleCheckFrames = 3;
        private static readonly Dictionary<string, Watch> Watches = new();

        /// <summary>
        /// There is no public API to ask "will scripts compile?" before requesting.
        /// <see cref="CompilationPipeline.RequestScriptCompilation"/> decides internally;
        /// this method observes <c>compilationStarted</c>/<c>compilationFinished</c> or,
        /// when no cycle starts, completes after a short idle window.
        /// </summary>
        public static void RequestWithCompletion(
            string commandId,
            Action<object> completeSuccess,
            Action<string> completeFail = null)
        {
            if (string.IsNullOrEmpty(commandId))
                throw new ArgumentException("commandId is required", nameof(commandId));
            if (completeSuccess == null)
                throw new ArgumentNullException(nameof(completeSuccess));

            RemoveWatch(commandId);

            var watch = new Watch(commandId, completeSuccess, completeFail);
            Watches[commandId] = watch;
            watch.Subscribe();
            CompilationPipeline.RequestScriptCompilation();
            EditorApplication.delayCall += watch.OnIdleCheckFrame;
        }

        private static void RemoveWatch(string commandId)
        {
            if (!Watches.TryGetValue(commandId, out var watch))
                return;
            watch.Dispose();
            Watches.Remove(commandId);
        }

        private sealed class Watch
        {
            private readonly string _commandId;
            private readonly Action<object> _completeSuccess;
            private readonly Action<string> _completeFail;

            private bool _completed;
            private bool _armed = true;
            private bool _sawCompilationStarted;
            private object _compilationContext;
            private int _idleFrames;

            public Watch(string commandId, Action<object> completeSuccess, Action<string> completeFail)
            {
                _commandId = commandId;
                _completeSuccess = completeSuccess;
                _completeFail = completeFail;
            }

            public void Subscribe()
            {
                CompilationPipeline.compilationStarted += OnCompilationStarted;
                CompilationPipeline.compilationFinished += OnCompilationFinished;
            }

            public void OnIdleCheckFrame()
            {
                if (_completed || !_armed)
                    return;

                _idleFrames++;
                if (_idleFrames < IdleCheckFrames)
                {
                    EditorApplication.delayCall += OnIdleCheckFrame;
                    return;
                }

                if (_sawCompilationStarted)
                {
                    if (!EditorApplication.isCompiling)
                        TryComplete("compilation_idle");
                    else
                        EditorApplication.delayCall += OnIdleCheckFrame;
                    return;
                }

                if (!EditorApplication.isCompiling)
                    TryComplete("no_compilation_needed");
                else
                    EditorApplication.delayCall += OnIdleCheckFrame;
            }

            private void OnCompilationStarted(object context)
            {
                if (!_armed || _completed)
                    return;

                _sawCompilationStarted = true;
                _compilationContext = context;
            }

            private void OnCompilationFinished(object context)
            {
                if (!_armed || _completed || !_sawCompilationStarted)
                    return;

                if (!ReferenceEquals(context, _compilationContext))
                    return;

                TryComplete("compilation_finished");
            }

            public bool TryComplete(string note)
            {
                if (_completed)
                    return false;

                _completed = true;
                _armed = false;
                Dispose();

                try
                {
                    _completeSuccess(CommandResult.Ok(
                        "compile completed",
                        new Dictionary<string, object>
                        {
                            ["compiled"] = true,
                            ["note"] = note,
                        }));
                }
                catch (Exception ex)
                {
                    _completeFail?.Invoke(ex.Message);
                }

                return true;
            }

            public void Dispose()
            {
                CompilationPipeline.compilationStarted -= OnCompilationStarted;
                CompilationPipeline.compilationFinished -= OnCompilationFinished;
                Watches.Remove(_commandId);
            }
        }
    }
}
