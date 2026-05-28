using UnityCliConnector.Commands;
using UnityCliConnector.Params;

namespace UnityCliConnector.Commands
{
    public class ExecCommand : CommandBase, ICommand<ExecParams>, ICommandDescriptorProvider
    {
        public CommandDescriptor Descriptor { get; } = new CommandDescriptor<ExecParams>(
            CommandNames.Exec,
            CommandScope.Editor,
            "Compile and execute arbitrary C# in Editor context");

        public void Run(ExecParams p)
        {
            try
            {
                var data = Editor.Services.CsharpExecutor.Execute(p);
                CompleteSuccess(CommandResult.Ok("exec completed", data));
            }
            catch (System.Exception ex)
            {
                CompleteFail(ex.Message);
            }
        }
    }
}
