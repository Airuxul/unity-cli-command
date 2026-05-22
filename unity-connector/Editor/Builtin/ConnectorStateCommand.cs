namespace UnityCliConnector.Builtin
{
    [CliCommand("connector.state", Scope = CommandScope.Editor, Description = "Editor state snapshot")]
    public static class ConnectorStateCommand
    {
        public static CommandResult Run(CliParams p) =>
            CommandResult.Success(EditorStateProvider.ToManifestObject());
    }
}
