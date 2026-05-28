using System;
using System.IO;
using System.Net;
using System.Threading;
using UnityCliConnector.Network;

namespace UnityCliConnector.Http
{
    public static class ConnectorHttpLifecycle
    {
        public static bool TryStart(
            ref HttpServer server,
            ref ConnectorListenConfig listen,
            ConnectorRequestDispatcher dispatcher,
            int port,
            string label,
            Action<string> log,
            Action<string> logError,
            Action onStarted = null)
        {
            const int maxAttempts = 20;
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    listen = ConnectorNetwork.BuildListenConfig(port);
                    server?.Dispose();
                    server = new HttpServer(dispatcher, log, logError);
                    server.Start(listen.BindPrefixes);
                    onStarted?.Invoke();
                    log?.Invoke(
                        $"[unity-connector] {label} ({listen.BindMode}) " +
                        $"http://{listen.AdvertiseHost}:{listen.Port}/");
                    return true;
                }
                catch (Exception ex)
                {
                    server?.Dispose();
                    server = null;

                    if (IsAddressInUseError(ex) && attempt < maxAttempts)
                    {
                        Thread.Sleep(150);
                        continue;
                    }

                    if (IsAddressInUseError(ex))
                    {
                        if (LooksLikeConnectorAlreadyRunning(listen))
                        {
                            log?.Invoke(
                                $"[unity-connector] {label} already running on http://{listen.AdvertiseHost}:{listen.Port}/ (reuse).");
                            onStarted?.Invoke();
                            return true;
                        }
                    }

                    logError?.Invoke($"[unity-connector] {label} failed: {ex.Message}");
                    return false;
                }
            }

            return false;
        }

        public static void Stop(ref HttpServer server) => server = Dispose(server);

        private static HttpServer Dispose(HttpServer server)
        {
            server?.Dispose();
            return null;
        }

        private static bool IsAddressInUseError(Exception ex)
        {
            if (ex == null)
                return false;

            var msg = ex.Message ?? "";
            if (msg.IndexOf("only one usage", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (msg.IndexOf("address already in use", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (msg.IndexOf("只允许使用一次", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (msg.IndexOf("套接字地址", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return IsAddressInUseError(ex.InnerException);
        }

        private static bool LooksLikeConnectorAlreadyRunning(ConnectorListenConfig listen)
        {
            if (listen == null)
                return false;

            var candidates = new[]
            {
                listen.AdvertiseHost,
                "127.0.0.1",
                "localhost",
            };

            foreach (var host in candidates)
            {
                if (string.IsNullOrWhiteSpace(host))
                    continue;
                if (ProbeHealth(host.Trim(), listen.Port))
                    return true;
            }

            return false;
        }

        private static bool ProbeHealth(string host, int port)
        {
            var url = $"http://{host}:{port}/health";
            try
            {
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "GET";
                request.Timeout = 500;
                request.ReadWriteTimeout = 500;
                request.KeepAlive = false;

                using var response = (HttpWebResponse)request.GetResponse();
                if ((int)response.StatusCode < 200 || (int)response.StatusCode >= 300)
                    return false;

                using var reader = new StreamReader(response.GetResponseStream() ?? Stream.Null);
                var body = reader.ReadToEnd();
                if (string.IsNullOrEmpty(body))
                    return false;

                return body.IndexOf("\"ok\":true", StringComparison.OrdinalIgnoreCase) >= 0
                    && body.IndexOf("connector_build", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
