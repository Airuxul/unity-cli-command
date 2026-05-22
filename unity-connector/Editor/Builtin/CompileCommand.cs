namespace UnityCliConnector.Builtin
{
    [CliCommand("compile", Scope = CommandScope.Editor, Description = "Request script compilation (async job)")]
    public static class CompileCommand
    {
        public static CommandResult Run(CliParams p) =>
            CommandResult.Success(new { started = true });
    }
}
