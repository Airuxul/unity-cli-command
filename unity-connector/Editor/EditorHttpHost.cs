using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace UnityCliConnector
{
    [InitializeOnLoad]
    public static class EditorHttpHost
    {
        private static HttpListener _listener;
        private static Thread _thread;
        private static volatile bool _running;

        static EditorHttpHost()
        {
            EditorApplication.quitting += Stop;
            Stop();
            Start();
        }

        public static void Start()
        {
            if (_running)
                return;

            try
            {
                var port = ResolvePort();
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                _listener.Start();
                _running = true;
                HeartbeatWriter.SetEndpoint("127.0.0.1", port);
                _thread = new Thread(ListenLoop) { IsBackground = true, Name = "UnityCliConnector.Http" };
                _thread.Start();
                Debug.Log($"[unity-connector] listening on http://127.0.0.1:{port}/");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[unity-connector] failed to start HTTP server: {ex.Message}");
            }
        }

        public static void Stop()
        {
            _running = false;
            try
            {
                _listener?.Stop();
                _listener?.Close();
            }
            catch
            {
                // ignored
            }

            _listener = null;
        }

        private static int ResolvePort()
        {
            var env = Environment.GetEnvironmentVariable("UNITY_CMD_PORT");
            if (int.TryParse(env, out var p) && p > 0)
                return p;

            var hash = Application.dataPath.GetHashCode();
            return 6400 + Math.Abs(hash % 800);
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
                catch (HttpListenerException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (_running)
                        Debug.LogWarning($"[unity-connector] accept error: {ex.Message}");
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
                    WriteJson(ctx, 200, new Dictionary<string, object>
                    {
                        ["ok"] = true,
                        ["host"] = "editor",
                        ["connector_build"] = 3,
                    });
                    return;
                }

                if (path == "/list" && method == "POST")
                {
                    WriteJson(ctx, 200, new Dictionary<string, object>
                    {
                        ["ok"] = true,
                        ["commands"] = CommandListBuilder.Build(),
                    });
                    return;
                }

                if (path.StartsWith("/jobs/", StringComparison.Ordinal) && method == "GET")
                {
                    var id = path.Substring("/jobs/".Length).Trim('/');
                    var job = JobManager.Get(id);
                    if (job == null)
                    {
                        WriteJson(ctx, 404, new Dictionary<string, object> { ["ok"] = false, ["error"] = "job_not_found" });
                        return;
                    }

                    WriteJson(ctx, 200, JobToResponse(job));
                    return;
                }

                if (path == "/command" && method == "POST")
                {
                    HandleCommand(ctx);
                    return;
                }

                WriteJson(ctx, 404, new Dictionary<string, object> { ["ok"] = false, ["error"] = "not_found" });
            }
            catch (Exception ex)
            {
                WriteJson(ctx, 500, new Dictionary<string, object> { ["ok"] = false, ["error"] = ex.Message });
            }
        }

        private static void HandleCommand(HttpListenerContext ctx)
        {
            var body = ReadBody(ctx.Request);
            var root = SimpleJson.ParseObject(body);
            var command = GetString(root, "command") ?? "";
            var dict = GetParameters(root);

            var cmdRequest = new CommandRequest
            {
                Command = command,
                Parameters = dict,
                RequestId = string.IsNullOrEmpty(GetString(root, "request_id"))
                    ? Guid.NewGuid().ToString("N")
                    : GetString(root, "request_id"),
                Endpoint = "editor",
            };

            var completion = CommandJobCatalog.GetCompletionKind(command, dict);
            if (completion != null)
            {
                try
                {
                    var accepted = EditorMainThread.Run(() =>
                    {
                        var job = JobManager.Create(command, completion, cmdRequest.RequestId);
                        EditorCommandExecutor.StartJobSideEffect(command, dict);
                        return new Dictionary<string, object>
                        {
                            ["ok"] = true,
                            ["job_id"] = job.Id,
                            ["completion"] = completion,
                            ["request_id"] = cmdRequest.RequestId,
                        };
                    }, TimeSpan.FromSeconds(30));

                    WriteJson(ctx, 202, accepted);
                }
                catch (Exception ex)
                {
                    WriteJson(ctx, 500, new Dictionary<string, object>
                    {
                        ["ok"] = false,
                        ["error"] = ex.Message,
                        ["request_id"] = cmdRequest.RequestId,
                    });
                }

                return;
            }

            var result = EditorCommandExecutor.ExecuteSync(cmdRequest);
            var status = result.Ok ? 200 : 400;
            WriteJson(ctx, status, new Dictionary<string, object>
            {
                ["ok"] = result.Ok,
                ["data"] = result.Data,
                ["error"] = result.Error,
                ["request_id"] = result.RequestId,
            });
        }

        private static Dictionary<string, object> JobToResponse(JobRecord job)
        {
            var dict = new Dictionary<string, object>
            {
                ["ok"] = true,
                ["job_id"] = job.Id,
                ["status"] = job.Status.ToString().ToLowerInvariant(),
                ["command"] = job.Command,
                ["error"] = job.Error,
                ["request_id"] = job.RequestId,
            };
            if (job.Result != null)
                dict["result"] = job.Result;
            return dict;
        }

        private static string ReadBody(HttpListenerRequest request)
        {
            if (!request.HasEntityBody)
                return "{}";
            using var reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8);
            return reader.ReadToEnd();
        }

        private static string GetString(Dictionary<string, object> dict, string key)
        {
            if (dict == null || !dict.TryGetValue(key, out var v) || v == null)
                return null;
            return v.ToString();
        }

        private static Dictionary<string, object> GetParameters(Dictionary<string, object> root)
        {
            if (root != null && root.TryGetValue("parameters", out var raw) &&
                raw is Dictionary<string, object> dict)
                return dict;
            return new Dictionary<string, object>();
        }

        private static void WriteJson(HttpListenerContext ctx, int status, object payload)
        {
            var json = payload is Dictionary<string, object> d
                ? SimpleJson.Serialize(d)
                : SimpleJson.Serialize(payload);
            var bytes = Encoding.UTF8.GetBytes(json);
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/json";
            ctx.Response.ContentEncoding = Encoding.UTF8;
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.Close();
        }

    }
}
