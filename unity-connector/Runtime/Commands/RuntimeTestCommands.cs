using UnityCliConnector.Commands;

namespace UnityCliConnector.Commands
{
    /// <summary>Test-only empty runtime command.</summary>
    public class Action1RuntimeCommand : CommandBase, ICommand, ICommandDescriptorProvider
    {
        public CommandDescriptor Descriptor { get; } = new CommandDescriptor(
            CommandNames.Action1,
            CommandScope.Runtime,
            "Runtime test action 1");

        public void Run()
        {
            CompleteSuccess(CommandResult.Ok());
        }
    }

    /// <summary>Test-only empty runtime command.</summary>
    public class Action2RuntimeCommand : CommandBase, ICommand, ICommandDescriptorProvider
    {
        public CommandDescriptor Descriptor { get; } = new CommandDescriptor(
            CommandNames.Action2,
            CommandScope.Runtime,
            "Runtime test action 2");

        public void Run()
        {
            CompleteSuccess(CommandResult.Ok());
        }
    }
}
