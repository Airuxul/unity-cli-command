using System;
using System.Collections.Generic;
using UnityCliConnector.Commands;
using UnityEditor;
using UnityCliConnector.Params;

namespace UnityCliConnector.Commands
{
    public class MenuCommand : CommandBase, ICommand<MenuParams>, ICommandDescriptorProvider
    {
        public CommandDescriptor Descriptor { get; } = new CommandDescriptor<MenuParams>(
            CommandNames.Menu,
            CommandScope.Editor,
            "Execute a Unity menu item by path");

        private static readonly HashSet<string> Blocklist = new(StringComparer.OrdinalIgnoreCase)
        {
            "File/Quit",
        };

        public void Run(MenuParams p)
        {
            var menuPath = p.MenuPath;
            if (string.IsNullOrWhiteSpace(menuPath))
            {
                const string error = "Parameter 'menu_path' is required (e.g. File/Save Project).";
                CompleteFail(error);
                return;
            }

            if (Blocklist.Contains(menuPath))
            {
                var error = $"Execution of '{menuPath}' is blocked for safety.";
                CompleteFail(error);
                return;
            }

            if (!EditorApplication.ExecuteMenuItem(menuPath))
            {
                var error = $"Failed to execute menu item '{menuPath}'.";
                CompleteFail(error);
                return;
            }

            CompleteSuccess(CommandResult.Ok($"menu executed: {menuPath}"));
        }
    }
}
