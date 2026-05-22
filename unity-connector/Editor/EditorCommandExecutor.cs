using System;
using System.Collections.Generic;
using System.Threading;
using UnityEditor;
using UnityEditor.Compilation;

namespace UnityCliConnector
{
    public static class EditorCommandExecutor
    {
        public static void StartJobSideEffect(string command, Dictionary<string, object> parameters)
        {
            switch (command)
            {
                case "compile":
                case "editor.recompile":
                    CompilationPipeline.RequestScriptCompilation();
                    break;
                case "editor.play":
                    EditorApplication.EnterPlaymode();
                    break;
                case "editor.stop":
                    EditorApplication.ExitPlaymode();
                    break;
                case "refresh":
                    AssetDatabase.Refresh();
                    if (parameters != null &&
                        parameters.TryGetValue("compile", out var c) &&
                        c is bool compile && compile)
                        CompilationPipeline.RequestScriptCompilation();
                    break;
            }
        }

        public static CommandResult ExecuteSync(CommandRequest request)
        {
            try
            {
                return EditorMainThread.Run(
                    () => SafeRoute(request),
                    TimeSpan.FromSeconds(30));
            }
            catch (TimeoutException)
            {
                return CommandResult.Fail(
                    "Command timed out waiting for main thread.",
                    request.RequestId);
            }
            catch (Exception ex)
            {
                return CommandResult.Fail(ex.Message, request.RequestId);
            }
        }

        private static CommandResult SafeRoute(CommandRequest request)
        {
            var result = CommandRouter.Route(request, EditorApplication.isPlaying, "editor");
            if (string.IsNullOrEmpty(result.RequestId))
                result.RequestId = request.RequestId;
            return result;
        }
    }
}
