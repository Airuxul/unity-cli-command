using UnityCliConnector.Commands;
using UnityCliConnector.Params;

namespace UnityCliConnector.Commands
{
    public class ReserializeCommand : CommandBase, ICommand<ReserializeParams>, ICommandDescriptorProvider
    {
        public CommandDescriptor Descriptor { get; } = new CommandDescriptor<ReserializeParams>(
            CommandNames.Reserialize,
            CommandScope.Editor,
            "Force reserialize assets (whole project or paths)");

        public void Run(ReserializeParams p)
        {
            try
            {
                var data = Editor.Services.ReserializeService.Reserialize(p);
                CompleteSuccess(CommandResult.Ok("reserialize completed", data));
            }
            catch (System.Exception ex)
            {
                CompleteFail(ex.Message);
            }
        }
    }
}
