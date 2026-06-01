using System;
using System.Collections.Generic;

namespace UnityCliConnector.Http
{
    /// <summary>Shared drain logic for Editor and play-mode main-thread HTTP queues.</summary>
    public static class MainThreadHttpWork
    {
        public enum Kind
        {
            Command,
            Catalog,
            CommandStatus,
        }

        public sealed class Item
        {
            public Kind Kind;
            public string Body;
            public string CommandId;
            public Action<int, Dictionary<string, object>> WriteJson;
        }

        public static void Process(
            Item item,
            ICommandHost host,
            Func<string, Dictionary<string, object>> getCommandStatus,
            Action onCatalogReady = null)
        {
            try
            {
                switch (item.Kind)
                {
                    case Kind.Catalog:
                        var catalog = CommandCatalog.BuildResponse(host.HostName);
                        onCatalogReady?.Invoke();
                        item.WriteJson(200, catalog);
                        break;

                    case Kind.CommandStatus:
                        var payload = getCommandStatus?.Invoke(item.CommandId);
                        if (payload == null)
                        {
                            item.WriteJson(404, new Dictionary<string, object>
                            {
                                ["ok"] = false,
                                ["error"] = "command_not_found",
                            });
                        }
                        else
                        {
                            item.WriteJson(200, payload);
                        }

                        break;

                    default:
                        var request = CommandHttpHelper.ParseCommandRequest(item.Body, host.HostName);
                        var post = host.HandleCommand(request);
                        item.WriteJson(post.StatusCode, post.Body);
                        break;
                }
            }
            catch (Exception ex)
            {
                item.WriteJson(500, new Dictionary<string, object>
                {
                    ["ok"] = false,
                    ["error"] = ex.Message,
                });
            }
        }
    }
}
