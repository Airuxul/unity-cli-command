using System.Collections.Generic;
using UnityCliConnector.Commands;

namespace UnityCliConnector
{
    public static class CommandResponseBuilder
    {
        public static Dictionary<string, object> ToResponse(CommandRecord command)
        {
            if (command == null) return null;
            var dict = new Dictionary<string, object>
            {
                ["ok"] = true,
                ["command_id"] = command.Id,
                ["status"] = command.Status.ToString().ToLowerInvariant(),
                ["command"] = command.Command,
                ["error"] = command.Error,
                ["request_id"] = command.RequestId,
            };
            var result = command.Result ?? ConnectorJson.Deserialize(command.ResultJson);
            if (result is CommandResult unified)
            {
                dict["result"] = unified.Payload;
                if (!string.IsNullOrWhiteSpace(unified.Code))
                    dict["code"] = unified.Code;
                if (!string.IsNullOrWhiteSpace(unified.Message))
                    dict["message"] = unified.Message;
            }
            else if (result != null)
            {
                dict["result"] = result;
            }
            return dict;
        }
    }
}
