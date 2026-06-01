using System;
using System.Collections.Generic;

namespace UnityCliConnector
{
    public static class EditorCommandExecutor
    {
        public static void ExecuteCommand(CommandContext context, Dictionary<string, object> parameters) =>
            CommandExecutor.ExecuteCommand(context, parameters);
    }
}
