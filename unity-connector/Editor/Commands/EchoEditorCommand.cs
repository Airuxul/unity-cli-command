using UnityCliConnector.Commands;
using UnityCliConnector.Params;

namespace UnityCliConnector.Commands
{
    public class EchoEditorCommand : CommandBase, ICommand<EchoParams>, ICommandDescriptorProvider
    {
        public CommandDescriptor Descriptor { get; } = new CommandDescriptor<EchoParams>(
            CommandNames.Echo,
            CommandScope.Editor,
            "Echo from Editor host");

        public void Run(EchoParams p)
        {
            var text = p?.Message ?? "ok";
            CompleteSuccess(CommandResult.Ok("echo ok", text));
        }
    }
}
