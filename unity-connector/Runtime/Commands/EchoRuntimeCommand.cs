using UnityCliConnector.Commands;
using UnityCliConnector.Params;

namespace UnityCliConnector.Commands
{
    public class EchoRuntimeCommand : CommandBase, ICommand<EchoParams>, ICommandDescriptorProvider
    {
        public CommandDescriptor Descriptor { get; } = new CommandDescriptor<EchoParams>(
            CommandNames.Echo,
            CommandScope.Runtime,
            "Echo from Runtime host");

        public void Run(EchoParams p)
        {
            var text = p?.Message ?? "ok";
            CompleteSuccess(CommandResult.Ok("echo ok", text));
        }
    }
}
