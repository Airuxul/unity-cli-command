using System.Collections.Generic;
using UnityCliConnector.Commands;
using UnityCliConnector.Editor.Services;
using UnityCliConnector.Params;

namespace UnityCliConnector.Commands
{
    public class ConsoleCommand : CommandBase, ICommand<ConsoleParams>, ICommandDescriptorProvider
    {
        public CommandDescriptor Descriptor { get; } = new CommandDescriptor<ConsoleParams>(
            CommandNames.Console,
            CommandScope.Editor,
            "Read or clear Unity Editor console logs");

        public void Run(ConsoleParams p)
        {
            if (!UnityConsoleReader.IsReady)
            {
                var error = $"Console reader unavailable: {UnityConsoleReader.InitError}";
                CompleteFail(error);
                return;
            }

            if (p.Clear)
            {
                UnityConsoleReader.Clear();
                CompleteSuccess(CommandResult.Ok("console cleared"));
                return;
            }

            var stacktrace = string.IsNullOrEmpty(p.Stacktrace) ? "user" : p.Stacktrace;
            var entries = UnityConsoleReader.Read(new UnityConsoleReader.ConsoleReadOptions
            {
                TypeFilter = p.Type ?? "error,warning",
                MaxEntries = p.Lines,
                Stacktrace = stacktrace,
            });

            var data = new Dictionary<string, object>
            {
                ["count"] = entries.Count,
                ["entries"] = entries,
                ["types"] = p.Type ?? "error,warning",
                ["stacktrace"] = stacktrace,
            };
            CompleteSuccess(CommandResult.Ok("console entries", data));
        }
    }
}
