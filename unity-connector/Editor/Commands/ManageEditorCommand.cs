using UnityCliConnector.Commands;
using UnityCliConnector.Params;

namespace UnityCliConnector.Commands
{
    public class ManageEditorCommand : CommandBase, ICommand<ManageEditorParams>, ICommandDescriptorProvider
    {
        public CommandDescriptor Descriptor { get; } = new CommandDescriptor<ManageEditorParams>(
            CommandNames.Manage,
            CommandScope.Editor,
            "Editor control: play, stop, pause, refresh, tags, layers, tools");

        public void Run(ManageEditorParams p)
        {
            try
            {
                var data = Editor.Services.EditorManageService.Execute(p);
                CompleteSuccess(CommandResult.Ok("manage completed", data));
            }
            catch (System.Exception ex)
            {
                CompleteFail(ex.Message);
            }
        }
    }
}
