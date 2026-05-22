namespace UnityCliConnector.Builtin
{
    [CliCommand("ping", Scope = CommandScope.Any, Description = "Health check")]
    public static class PingCommand
    {
        public static CommandResult Run(CliParams p) =>
            CommandResult.Success(new System.Collections.Generic.Dictionary<string, object>
            {
                ["pong"] = true,
                ["host"] = "editor",
            });
    }
}
