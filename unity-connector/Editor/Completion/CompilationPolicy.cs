namespace UnityCliConnector.Completion
{
    /// <summary>
    /// Fallback completion when <see cref="Editor.Services.ScriptCompilationService"/> did not
    /// finish the command (e.g. domain reload). Succeeds when Unity is not compiling.
    /// </summary>
    public sealed class CompilationPolicy : ICompletionPolicy
    {
        public string Kind => CommandCompletionCatalog.CompletionCompilation;

        public bool TryComplete(CommandRecord command, EditorStateSnapshot state, out object result, out string error)
        {
            result = null;
            error = null;

            if (state.IsCompiling)
            {
                if (command.Status == CommandStatus.Pending)
                    command.Status = CommandStatus.Running;
                return false;
            }

            if (command.Status is CommandStatus.Pending or CommandStatus.Running)
            {
                result = new System.Collections.Generic.Dictionary<string, object>
                {
                    ["compiled"] = true,
                    ["note"] = command.Status == CommandStatus.Pending ? "already_idle" : "compilation_idle",
                };
                return true;
            }

            return false;
        }
    }
}
