using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using UnityEngine;

namespace UnityCliConnector
{
    public static class RuntimeHttpHost
    {
        private static HttpListener _listener;
        private static Thread _thread;
        private static volatile bool _running;
        private static int _port;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
#if UNITY_EDITOR
            return;
#else
            if (!Debug.isDebugBuild)
                return;
            Start();
#endif
        }

        public static void Start()
        {
            if (_running)
                return;

            try
            {
                _port = ResolvePort();
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
                _listener.Start();
                _running = true;
                _thread = new Thread(ListenLoop) { IsBackground = true };
                _thread.Start();
                Debug.Log($"[unity-connector] runtime listening on http://127.0.0.1:{_port}/");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[unity-connector] runtime HTTP failed: {ex.Message}");
            }
        }

        private static int ResolvePort()
        {
            var env = Environment.GetEnvironmentVariable("UNITY_CMD_RUNTIME_PORT");
            if (int.TryParse(env, out var p) && p > 0)
                return p;
            return 6500 + Math.Abs(Application.dataPath.GetHashCode() % 800);
        }

        private static void ListenLoop()
        {
            while (_running)
            {
                try
                {
                    var ctx = _listener.GetContext();
                    ThreadPool.QueueUserWorkItem(_ => Handle(ctx));
                }
                catch
                {
                    break;
                }
            }
        }

        private static void Handle(HttpListenerContext ctx)
        {
            try
            {
                var path = ctx.Request.Url?.AbsolutePath ?? "/";
                var method = ctx.Request.HttpMethod?.ToUpperInvariant() ?? "GET";

                if (path == "/health" && method == "GET")
                {
                    WriteJson(ctx, 200, new Dictionary<string, object> { ["ok"] = true, ["host"] = "runtime" });
                    return;
                }

                if (path == "/command" && method == "POST")
                {
                    var body = ReadBody(ctx.Request);
                    var root = SimpleJson.ParseObject(body);
                    var command = root.TryGetValue("command", out var c) ? c?.ToString() : "";
                    Dictionary<string, object> parameters = root.TryGetValue("parameters", out var p) &&
                                                             p is Dictionary<string, object> d
                        ? d
                        : new Dictionary<string, object>();

                    var request = new CommandRequest
                    {
                        Command = command,
                        Parameters = parameters,
                        RequestId = root.TryGetValue("request_id", out var rid) ? rid?.ToString() : Guid.NewGuid().ToString("N"),
                        Endpoint = "runtime",
                    };

                    var result = CommandRouter.Route(request, true, "runtime");
                    WriteJson(ctx, result.Ok ? 200 : 400, new Dictionary<string, object>
                    {
                        ["ok"] = result.Ok,
                        ["data"] = result.Data,
                        ["error"] = result.Error,
                        ["request_id"] = result.RequestId,
                    });
                    return;
                }

                WriteJson(ctx, 404, new Dictionary<string, object> { ["ok"] = false, ["error"] = "not_found" });
            }
            catch (Exception ex)
            {
                WriteJson(ctx, 500, new Dictionary<string, object> { ["ok"] = false, ["error"] = ex.Message });
            }
        }

        private static string ReadBody(HttpListenerRequest request)
        {
            if (!request.HasEntityBody)
                return "{}";
            using var reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8);
            return reader.ReadToEnd();
        }

        private static void WriteJson(HttpListenerContext ctx, int status, Dictionary<string, object> payload)
        {
            var bytes = Encoding.UTF8.GetBytes(SimpleJson.Serialize(payload));
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/json";
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.Close();
        }
    }
}
