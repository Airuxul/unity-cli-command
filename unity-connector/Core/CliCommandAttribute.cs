using System;

namespace UnityCliConnector
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class CliCommandAttribute : Attribute
    {
        public string Name { get; }
        public CommandScope Scope { get; set; } = CommandScope.Editor;
        public string Description { get; set; } = "";
        public bool IsJob { get; set; }
        public string Completion { get; set; }

        public CliCommandAttribute(string name)
        {
            Name = name;
        }
    }
}
