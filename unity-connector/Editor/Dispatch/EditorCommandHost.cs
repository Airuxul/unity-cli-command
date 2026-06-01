using System.Collections.Generic;
using UnityCliConnector.Http;

namespace UnityCliConnector
{
    public sealed class EditorCommandHost : ICommandHost
    {
        public static readonly EditorCommandHost Instance = new();
        private static readonly ICommandNotifier Notifier = new CommandNotifier(
            CommandStateManager.MarkRunning,
            CommandStateManager.Succeed,
            CommandStateManager.Fail);

        public string HostName => "editor";

        public CommandPipeline.PostResult HandleCommand(CommandRequest request) =>
            CommandPipeline.HandleUnifiedPost(
                request,
                CreateContext,
                EditorCommandExecutor.ExecuteCommand);

        private static (string commandId, CommandContext context) CreateContext(CommandRequest request, string completion)
        {
            var command = CommandStateManager.Create(request.Command, completion, request.RequestId);
            var ctx = new CommandContext
            {
                CommandId = command.Id,
                RequestId = request.RequestId,
                Command = request.Command,
                HostName = Instance.HostName,
                Notifier = Notifier,
            };
            return (command.Id, ctx);
        }
    }
}
