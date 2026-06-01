using System.Collections.Generic;
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
                var data = AssetRefreshService.Refresh(p);
                if (!p.Compile)
                {
                    CompleteSuccess(CommandResult.Ok("refresh completed", data));
                    return;
                }

                ScriptCompilationService.RequestWithCompletion(
                    CommandId,
                    result => CompleteSuccess(MergeRefreshResult(result, data)),
                    CompleteFail);
            }
            catch (System.Exception ex)
            {
                CompleteFail(ex.Message);
            }
        }

        private static object MergeRefreshResult(object compileResult, Dictionary<string, object> refreshData)
        {
            if (compileResult is not CommandResult compile || refreshData == null)
                return compileResult;

            var payload = compile.Payload as Dictionary<string, object> ?? new Dictionary<string, object>();
            foreach (var pair in refreshData)
                payload[pair.Key] = pair.Value;
            payload["compile_requested"] = true;

            return CommandResult.Ok(compile.Message, payload);
        }
    }
}
