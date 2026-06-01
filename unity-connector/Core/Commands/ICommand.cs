namespace UnityCliConnector.Commands
{
    public interface ICommandRuntime
    {
        string CommandId { get; }
        void CompleteSuccess(object result);
        void CompleteFail(string error);
        void MarkRunning();
    }

    public abstract class CommandBase
    {
        private ICommandRuntime _runtime;

        internal void BindRuntime(ICommandRuntime runtime) => _runtime = runtime;

        protected string CommandId => _runtime?.CommandId;
        protected void CompleteSuccess(object result) => _runtime?.CompleteSuccess(result);
        protected void CompleteFail(string error) => _runtime?.CompleteFail(error);
        protected void MarkRunning() => _runtime?.MarkRunning();
    }

    public interface ICommandDescriptorProvider
    {
        CommandDescriptor Descriptor { get; }
    }

    /// <summary>Synchronous command with no parameters.</summary>
    public interface ICommand
    {
        void Run();
    }

    /// <summary>Synchronous command with typed parameters.</summary>
    public interface ICommand<TParams>
    {
        void Run(TParams cliParams);
    }
}
