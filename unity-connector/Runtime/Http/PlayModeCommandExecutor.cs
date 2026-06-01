using System.Collections.Generic;

namespace UnityCliConnector
{
    internal static class PlayModeCommandExecutor
    {
        public static void ExecuteCommand(CommandContext context, Dictionary<string, object> parameters) =>
            CommandExecutor.ExecuteCommand(context, parameters);
    }
}
