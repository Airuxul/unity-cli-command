namespace UnityCliConnector
{
    public interface ICommandHandler
    {
        string Name { get; }
        CommandScope Scope { get; }
        string Description { get; }
        CommandResult Execute(CliParams parameters);
    }
}
