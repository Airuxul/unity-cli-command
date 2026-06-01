using System;
using System.Threading;
using UnityCliConnector.Network;

namespace UnityCliConnector.Http
{
    public static class ConnectorHttpLifecycle
    {
        private const int MaxAttempts = 8;

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
            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                try
                {
                    listen = ConnectorNetwork.BuildListenConfig(port);
                    server?.Dispose();
                    server = new HttpServer(dispatcher, log, logError);
                    server.Start(listen.BindPrefixes);
                    onStarted?.Invoke();
                    return true;
                }
                catch (Exception ex)
                {
                    server?.Dispose();
                    server = null;

                    if (!IsAddressInUseError(ex) || attempt >= MaxAttempts)
                    {
                        logError?.Invoke(
                            $"[unity-connector] {label} failed on port {port} after {attempt} attempt(s): {ex.Message}");
                        return false;
                    }

                    Thread.Sleep(BackoffMs(attempt));
                }
            }

            return false;
        }

        public static void Stop(ref HttpServer server)
        {
            server?.Dispose();
            server = null;
        }

        private static int BackoffMs(int attempt) => Math.Min(50 * attempt, 400);

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
    }
}
