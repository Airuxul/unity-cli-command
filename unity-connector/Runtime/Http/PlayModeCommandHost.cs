using System.Collections.Generic;
using UnityCliConnector.Http;

namespace UnityCliConnector
{
    public sealed class PlayModeCommandHost : ICommandHost
    {
        public PlayModeCommandHost(string hostName) => HostName = hostName;

        public string HostName { get; }

        public CommandPipeline.PostResult HandleCommand(CommandRequest request) =>
            CommandPipeline.HandleUnifiedPost(
                request,
                CreateContext,
                PlayModeCommandExecutor.ExecuteCommand);

        private (string commandId, CommandContext context) CreateContext(CommandRequest request, string completion)
        {
            var command = RuntimeCommandStateManager.Create(HostName, request.Command, completion, request.RequestId);
            var notifier = new CommandNotifier(
                id => RuntimeCommandStateManager.MarkRunning(HostName, id),
                (id, result) => RuntimeCommandStateManager.Succeed(HostName, id, result),
                (id, error) => RuntimeCommandStateManager.Fail(HostName, id, error));

            var context = new CommandContext
            {
                CommandId = command.Id,
                RequestId = request.RequestId,
                Command = request.Command,
                HostName = HostName,
                Notifier = notifier,
            };

            return (command.Id, context);
        }
    }
}
