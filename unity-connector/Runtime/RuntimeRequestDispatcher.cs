using System;
using System.Collections.Generic;
using UnityCliConnector.Http;

namespace UnityCliConnector
{
    public sealed class RuntimeRequestDispatcher : IRequestDispatcher
    {
        public bool TryDispatch(string method, string path, string body, Action<int, Dictionary<string, object>> writeJson)
        {
            if (path == "/health" && method == "GET")
            {
                writeJson(200, new Dictionary<string, object>
                {
                    ["ok"] = true,
                    ["host"] = RuntimeCommandHost.Instance.HostName,
                });
                return true;
            }

            if (path == "/command" && method == "POST")
            {
                var request = CommandHttpHelper.ParseCommandRequest(body, RuntimeCommandHost.Instance.HostName);
                var post = RuntimeCommandHost.Instance.HandleCommand(request);
                writeJson(post.StatusCode, post.Body);
                return true;
            }

            return false;
        }
    }
}
