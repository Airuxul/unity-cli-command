using System;
using System.Collections.Generic;

namespace UnityCliConnector
{
    public sealed class EditorCommandHost : ICommandHost
    {
        public static readonly EditorCommandHost Instance = new();

        public string HostName => "editor";

        public Http.CommandPipeline.PostResult HandleCommand(CommandRequest request) =>
            Http.CommandPipeline.HandlePost(
                request,
                CommandJobCatalog.GetCompletionKind,
                AcceptJob,
                EditorCommandExecutor.ExecuteSync);

        private static Dictionary<string, object> AcceptJob(CommandRequest request, string completion)
        {
            return EditorMainThread.Run(() =>
            {
                var job = JobManager.Create(request.Command, completion, request.RequestId);
                EditorCommandExecutor.StartJobSideEffect(request.Command, request.Parameters);
                return new Dictionary<string, object>
                {
                    ["ok"] = true,
                    ["job_id"] = job.Id,
                    ["completion"] = completion,
                    ["request_id"] = request.RequestId,
                };
            }, TimeSpan.FromSeconds(30));
        }
    }
}
