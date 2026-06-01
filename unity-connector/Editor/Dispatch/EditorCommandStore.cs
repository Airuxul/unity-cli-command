using System.Collections.Generic;
using UnityCliConnector.Http;

namespace UnityCliConnector
{
    internal sealed class EditorCommandStore : ICommandQuery
    {
        public static readonly EditorCommandStore Instance = new();

        public Dictionary<string, object> GetCommandResponse(string commandId) =>
            CommandResponseBuilder.ToResponse(CommandStateManager.Get(commandId));
    }
}
