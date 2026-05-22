namespace UnityCliConnector
{
    public static class CommandRouter
    {
        public static CommandResult Route(CommandRequest request, bool isPlaying, string hostKind)
        {
            var handler = CommandDiscovery.Find(request.Command);
            if (handler == null)
                return CommandResult.Fail($"Unknown command: {request.Command}", request.RequestId);

            if (hostKind == "runtime")
            {
                if (handler.Scope == CommandScope.Editor)
                    return CommandResult.Fail(
                        $"Command '{request.Command}' is Editor-only.",
                        request.RequestId);
            }
            else
            {
                if (handler.Scope == CommandScope.Runtime && !isPlaying)
                    return CommandResult.Fail(
                        $"Command '{request.Command}' requires Play Mode.",
                        request.RequestId);

                if (isPlaying && handler.Scope == CommandScope.Editor)
                    return CommandResult.Fail(
                        $"Command '{request.Command}' cannot run in Play Mode (Editor scope).",
                        request.RequestId);
            }

            var parameters = request.Parameters ?? new System.Collections.Generic.Dictionary<string, object>();
            return handler.Execute(new CliParams(parameters));
        }
    }
}
