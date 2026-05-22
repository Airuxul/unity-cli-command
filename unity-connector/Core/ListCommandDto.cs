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
            var list = new List<Dictionary<string, object>>();
            foreach (var handler in CommandDiscovery.Handlers)
            {
                var completion = CommandJobCatalog.GetCompletionKind(handler.Name, null);
                list.Add(new Dictionary<string, object>
                {
                    ["name"] = handler.Name,
                    ["scope"] = handler.Scope.ToString().ToLowerInvariant(),
                    ["description"] = handler.Description ?? "",
                    ["is_job"] = completion != null,
                    ["completion"] = completion,
                });
            }

            return list;
        }
    }
}
