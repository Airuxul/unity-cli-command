using System.Collections.Generic;

namespace UnityCliConnector
{
    public static class CommandJobCatalog
    {
        public const string CompletionCompilation = "compilation";
        public const string CompletionEnterPlay = "enter_play";
        public const string CompletionExitPlay = "exit_play";

        private static readonly Dictionary<string, string> BuiltIn = new()
        {
            { "compile", CompletionCompilation },
            { "editor.recompile", CompletionCompilation },
            { "editor.play", CompletionEnterPlay },
            { "editor.stop", CompletionExitPlay },
        };

        public static string GetCompletionKind(string command, Dictionary<string, object> parameters)
        {
            if (string.IsNullOrEmpty(command))
                return null;

            if (command == "refresh" && parameters != null &&
                parameters.TryGetValue("compile", out var compile) &&
                compile is bool b && b)
                return CompletionCompilation;

            return BuiltIn.TryGetValue(command, out var kind) ? kind : null;
        }

        public static bool IsJobCommand(string command, Dictionary<string, object> parameters) =>
            !string.IsNullOrEmpty(GetCompletionKind(command, parameters));
    }
}
