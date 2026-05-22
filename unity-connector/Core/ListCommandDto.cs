using System.Collections.Generic;

namespace UnityCliConnector
{
    public sealed class CommandListEntry
    {
        public string Name { get; set; }
        public string Scope { get; set; }
        public string Description { get; set; }
        public bool IsJob { get; set; }
        public string Completion { get; set; }
    }

    public static class CommandListBuilder
    {
        public static List<Dictionary<string, object>> Build()
        {
            var response = CommandCatalog.BuildResponse();
            if (response.TryGetValue("commands", out var raw) &&
                raw is List<Dictionary<string, object>> list)
                return list;
            return new List<Dictionary<string, object>>();
        }
    }
}
