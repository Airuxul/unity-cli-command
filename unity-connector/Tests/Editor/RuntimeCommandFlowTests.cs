using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using UnityCliConnector.Commands;
using UnityCliConnector.Network;
using UnityEngine;

namespace UnityCliConnector.Tests
{
    public class RuntimeCommandFlowTests
    {
        private const string CommandName = "test.runtime.command.flow";

        [Test]
        public void RuntimeCommand_AcceptsAndCompletes_OnEditorPlayHost()
        {
            CommandDiscovery.Invalidate();
            var host = new PlayModeCommandHost(ConnectorHostKind.EditorPlay);
            var request = new CommandRequest
            {
                Command = CommandName,
                Parameters = new Dictionary<string, object>(),
                RequestId = "req-runtime-command-flow",
                Endpoint = ConnectorHostKind.EditorPlay,
            };

            var post = host.HandleCommand(request);
            Assert.AreEqual(202, post.StatusCode);
            Assert.IsTrue(post.Body.TryGetValue("command_id", out var commandIdRaw));
            var commandId = commandIdRaw as string;
            Assert.IsNotNull(commandId);

            var command = WaitForCommand(ConnectorHostKind.EditorPlay, commandId, 7000);
            Assert.IsNotNull(command);
            Assert.AreEqual(CommandStatus.Succeeded, command.Status);

            var payload = CommandResponseBuilder.ToResponse(command);
            Assert.IsNotNull(payload);
            Assert.AreEqual("succeeded", payload["status"]);
            Assert.IsTrue(payload.ContainsKey("result"));
        }

        private sealed class RuntimeFlowParams { }

        private sealed class RuntimeFlowCommand : CommandBase, ICommandDescriptorProvider, ICommand<RuntimeFlowParams>
        {
            public CommandDescriptor Descriptor { get; } = new DeferredCommandDescriptor<RuntimeFlowParams>(
                CommandName, CommandScope.Runtime, "runtime command flow test command");

            public void Run(RuntimeFlowParams cliParams)
            {
                MarkRunning();
                _ = System.Threading.Tasks.Task.Run(() =>
                {
                    Debug.Log("[unity-connector][test] runtime command started");
                    Thread.Sleep(5000);
                    Debug.Log("[unity-connector][test] runtime command finished");
                    CompleteSuccess(CommandResult.Ok("runtime command finished"));
                });
            }
        }

        private static CommandRecord WaitForCommand(string host, string commandId, int timeoutMs)
        {
            var waited = 0;
            while (waited <= timeoutMs)
            {
                var command = RuntimeCommandStateManager.Get(host, commandId);
                if (command != null && command.Status is CommandStatus.Succeeded or CommandStatus.Failed or CommandStatus.Orphaned)
                    return command;
                Thread.Sleep(100);
                waited += 100;
            }

            return RuntimeCommandStateManager.Get(host, commandId);
        }
    }
}
