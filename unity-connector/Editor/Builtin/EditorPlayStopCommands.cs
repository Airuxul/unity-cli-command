namespace UnityCliConnector.Builtin
{
    [CliCommand("editor.play", Scope = CommandScope.Editor, Description = "Enter Play Mode (async job)")]
    public static class EditorPlayCommand
    {
        public static CommandResult Run(CliParams p) =>
            CommandResult.Success(new { started = true });
    }

    [CliCommand("editor.stop", Scope = CommandScope.Editor, Description = "Exit Play Mode (async job)")]
    public static class EditorStopCommand
    {
        public static CommandResult Run(CliParams p) =>
            CommandResult.Success(new { started = true });
    }
}
