namespace UnityCliConnector.Builtin
{
    [CliCommand("echo.runtime", Scope = CommandScope.Runtime, Description = "Echo from Runtime host")]
    public static class EchoRuntimeCommand
    {
        public static CommandResult Run(CliParams p) =>
            CommandResult.Success(new System.Collections.Generic.Dictionary<string, object>
            {
                ["channel"] = "runtime",
                ["message"] = p.GetString("message", "ok"),
            });
    }
}
