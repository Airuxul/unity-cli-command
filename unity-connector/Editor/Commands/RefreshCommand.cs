using UnityCliConnector.Commands;
using UnityCliConnector.Editor.Services;
using UnityCliConnector.Params;

namespace UnityCliConnector.Commands
{
    /// <summary>Unified command: immediate complete for refresh-only, deferred for compile=true.</summary>
    public class RefreshCommand : CommandBase, ICommand<RefreshParams>, ICommandDescriptorProvider
    {
        public CommandDescriptor Descriptor { get; } = new DeferredCommandDescriptor<RefreshParams>(
            CommandNames.Refresh,
            CommandScope.Editor,
            "Refresh AssetDatabase; compile=true uses compilation completion",
            completion: CommandCompletionCatalog.CompletionCompilation);

        public void Run(RefreshParams p)
        {
            try
            {
                var data = Apply(p);
                if (!p.Compile)
                    CompleteSuccess(CommandResult.Ok("refresh completed", data));
            }
            catch (System.Exception ex)
            {
                CompleteFail(ex.Message);
            }
        }

        private static System.Collections.Generic.Dictionary<string, object> Apply(RefreshParams p) =>
            AssetRefreshService.Refresh(p);
    }
}
