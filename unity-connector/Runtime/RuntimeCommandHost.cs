using System.Collections.Generic;

namespace UnityCliConnector
{
    public sealed class RuntimeCommandHost : ICommandHost
    {
        public static readonly RuntimeCommandHost Instance = new();

        public string HostName => "runtime";

        public Http.CommandPipeline.PostResult HandleCommand(CommandRequest request)
        {
            var result = CommandRouter.Route(request, true, HostName);
            return new Http.CommandPipeline.PostResult
            {
                StatusCode = result.Ok ? 200 : 400,
                Body = new Dictionary<string, object>
                {
                    ["ok"] = result.Ok,
                    ["data"] = result.Data,
                    ["error"] = result.Error,
                    ["request_id"] = result.RequestId,
                },
            };
        }
    }
}
