using UnityCliConnector.Commands;
using UnityCliConnector.Params;

namespace UnityCliConnector.Commands
{
    public class ConnectorStateCommand : CommandBase, ICommand, ICommandDescriptorProvider
    {
        public CommandDescriptor Descriptor { get; } = new CommandDescriptor(
            CommandNames.State,
            CommandScope.Editor,
            "Editor state snapshot");

        public void Run()
        {
            var data = EditorStateProvider.ToManifestObject();
            CompleteSuccess(CommandResult.Ok("state snapshot", data));
        }
    }
}
