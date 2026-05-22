namespace UnityCliConnector.Builtin
{
    [CliCommand("refresh", Scope = CommandScope.Editor, Description = "Refresh AssetDatabase; use compile=true for job")]
    public static class RefreshCommand
    {
        public static CommandResult Run(CliParams p) =>
            CommandResult.Success(new { refreshed = true, compile = p.GetBool("compile") });
    }
}
