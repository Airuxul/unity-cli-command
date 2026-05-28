using UnityCliConnector.Commands;
using UnityCliConnector.Params;

namespace UnityCliConnector.Commands
{
    public class ProfilerCommand : CommandBase, ICommand<ProfilerParams>, ICommandDescriptorProvider
    {
        public CommandDescriptor Descriptor { get; } = new CommandDescriptor<ProfilerParams>(
            CommandNames.Profiler,
            CommandScope.Editor,
            "Unity Profiler: hierarchy, enable, disable, status, clear");

        public void Run(ProfilerParams p)
        {
            try
            {
                var data = Editor.Services.ProfilerHierarchyService.Execute(p);
                CompleteSuccess(CommandResult.Ok("profiler completed", data));
            }
            catch (System.Exception ex)
            {
                CompleteFail(ex.Message);
            }
        }
    }
}
