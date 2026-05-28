using UnityCliConnector.Commands;
using UnityCliConnector.Params;
using UnityEngine;

namespace UnityCliConnector.Commands
{
    public class DebugRuntimeCommand : CommandBase, ICommand<RuntimeLogParams>, ICommandDescriptorProvider
    {
        public CommandDescriptor Descriptor { get; } = new CommandDescriptor<RuntimeLogParams>(
            CommandNames.Debug,
            CommandScope.Runtime,
            "Print runtime debug log");

        public void Run(RuntimeLogParams p)
        {
            var text = RuntimeLogCommandSupport.BuildText(p);
            Debug.Log(text);
            CompleteSuccess(CommandResult.Ok(text));
        }
    }

    public class WarnRuntimeCommand : CommandBase, ICommand<RuntimeLogParams>, ICommandDescriptorProvider
    {
        public CommandDescriptor Descriptor { get; } = new CommandDescriptor<RuntimeLogParams>(
            CommandNames.Warn,
            CommandScope.Runtime,
            "Print runtime warning log");

        public void Run(RuntimeLogParams p)
        {
            var text = RuntimeLogCommandSupport.BuildText(p);
            Debug.LogWarning(text);
            CompleteSuccess(CommandResult.Ok(text));
        }
    }

    public class ErrorRuntimeCommand : CommandBase, ICommand<RuntimeLogParams>, ICommandDescriptorProvider
    {
        public CommandDescriptor Descriptor { get; } = new CommandDescriptor<RuntimeLogParams>(
            CommandNames.Error,
            CommandScope.Runtime,
            "Print runtime error log");

        public void Run(RuntimeLogParams p)
        {
            var text = RuntimeLogCommandSupport.BuildText(p);
            Debug.LogError(text);
            CompleteSuccess(CommandResult.Ok(text));
        }
    }

    internal static class RuntimeLogCommandSupport
    {
        public static string BuildText(RuntimeLogParams p)
        {
            var message = p?.Message;
            if (string.IsNullOrWhiteSpace(message))
                message = "runtime log";

            if (!string.IsNullOrWhiteSpace(p?.Tag))
                return $"[{p.Tag}] {message}";

            return message;
        }

    }

}
