namespace UnityCliConnector.Builtin
{
    [CliCommand("echo.editor", Scope = CommandScope.Editor, Description = "Echo from Editor host")]
    public static class EchoEditorCommand
    {
        public static CommandResult Run(CliParams p) =>
            CommandResult.Success(new System.Collections.Generic.Dictionary<string, object>
            {
                ["channel"] = "editor",
                ["message"] = p.GetString("message", "ok"),
            });
    }
}
