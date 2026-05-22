namespace UnityCliConnector.Builtin
{
    [CliCommand(
        "editor.recompile",
        Scope = CommandScope.Editor,
        Description = "Recompile scripts (async job, alias of compile)")]
    public static class EditorRecompileCommand
    {
        public static CommandResult Run(CliParams p) =>
            CommandResult.Success(new System.Collections.Generic.Dictionary<string, object>
            {
                ["started"] = true,
                ["action"] = "recompile",
            });
    }
}
