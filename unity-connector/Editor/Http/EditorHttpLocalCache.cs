using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using UnityCliConnector.Http;
using UnityCliConnector.Network;

namespace UnityCliConnector
{
    /// <summary>
    /// Disk cache (~/.unity-cmd/editor-http.json) for a single Editor HTTP listener per machine/port.
    /// Used on domain reload and restart to detect orphans and avoid duplicate servers.
    /// </summary>
    internal static class EditorHttpLocalCache
    {
        private const string CacheFileName = "editor-http.json";
        private static readonly object FileGate = new();

        public enum StartupAction
        {
            StartFresh,
            WaitForPortRelease,
            ForeignProcessOwnsPort,
        }

        public enum PrepareResult
        {
            Proceed,
            PortOwnedByOtherProcess,
        }

        public sealed class Snapshot
        {
            public int Pid;
            public string SessionId;
            public int Generation;
            public int Port;
            public int ConnectorBuild;
            public string ListenerId;
            public string Status;
            public string UpdatedAtUtc;
        }

        private static string CachePath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".unity-cmd",
                CacheFileName);

        public static StartupAction ReconcileOnDomainStart(string sessionId, int generation, int port)
        {
            lock (FileGate)
            {
                var cache = LoadUnsafe();
                var pid = CurrentPid();

                if (cache == null)
                    return StartupAction.StartFresh;

                if (cache.Port != port)
                {
                    WriteUnsafe(MakeStopped(cache, pid, sessionId, generation, port));
                    return StartupAction.StartFresh;
                }

                if (cache.Pid != pid)
                {
                    if (!IsProcessAlive(cache.Pid))
                    {
                        ClearUnsafe();
                        return StartupAction.StartFresh;
                    }

                    if (cache.Status == "running" && ProbeSessionOnPort(port, cache.SessionId, cache.ConnectorBuild))
                        return StartupAction.ForeignProcessOwnsPort;

                    return StartupAction.StartFresh;
                }

                if (cache.Status == "running" && cache.SessionId != sessionId)
                    return StartupAction.WaitForPortRelease;

                return StartupAction.StartFresh;
            }
        }

        public static PrepareResult PrepareForStart(string sessionId, int generation, int port)
        {
            lock (FileGate)
            {
                var pid = CurrentPid();
                var cache = LoadUnsafe();

                if (cache != null && cache.Port == port && cache.Pid != pid && IsProcessAlive(cache.Pid))
                {
                    if (cache.Status == "running"
                        && ProbeSessionOnPort(port, cache.SessionId, cache.ConnectorBuild))
                    {
                        return PrepareResult.PortOwnedByOtherProcess;
                    }
                }

                if (cache != null
                    && cache.Port == port
                    && cache.Pid == pid
                    && cache.Status == "running"
                    && cache.SessionId != sessionId)
                {
                    WriteUnsafe(MakeStopped(cache, pid, sessionId, generation, port));
                    WaitForPortRelease(port, 3000);
                }

                if (!IsProcessAlive(pid) || (cache != null && cache.Pid != pid && !IsProcessAlive(cache.Pid)))
                    ClearUnsafe();

                return PrepareResult.Proceed;
            }
        }

        public static void MarkRunning(string sessionId, int generation, int port, string listenerId)
        {
            lock (FileGate)
            {
                var payload = new Dictionary<string, object>
                {
                    ["pid"] = CurrentPid(),
                    ["session_id"] = sessionId,
                    ["generation"] = generation,
                    ["port"] = port,
                    ["connector_build"] = ConnectorBuild.Id,
                    ["listener_id"] = listenerId,
                    ["status"] = "running",
                    ["updated_at_utc"] = DateTime.UtcNow.ToString("o"),
                };
                WriteUnsafe(payload);
            }
        }

        public static void MarkStopped(string sessionId, int generation, int port)
        {
            lock (FileGate)
            {
                var cache = LoadUnsafe();
                var payload = cache != null
                    ? MakeStopped(cache, CurrentPid(), sessionId, generation, port)
                    : new Dictionary<string, object>
                    {
                        ["pid"] = CurrentPid(),
                        ["session_id"] = sessionId,
                        ["generation"] = generation,
                        ["port"] = port,
                        ["connector_build"] = ConnectorBuild.Id,
                        ["listener_id"] = "",
                        ["status"] = "stopped",
                        ["updated_at_utc"] = DateTime.UtcNow.ToString("o"),
                    };
                WriteUnsafe(payload);
            }
        }

        public static void Clear()
        {
            lock (FileGate)
            {
                ClearUnsafe();
            }
        }

        public static bool MatchesRunningListener(string sessionId, int port, string listenerId)
        {
            lock (FileGate)
            {
                var cache = LoadUnsafe();
                return cache != null
                    && cache.Status == "running"
                    && cache.Port == port
                    && cache.Pid == CurrentPid()
                    && string.Equals(cache.SessionId, sessionId, StringComparison.Ordinal)
                    && string.Equals(cache.ListenerId, listenerId, StringComparison.Ordinal);
            }
        }

        private static Dictionary<string, object> MakeStopped(
            Snapshot cache,
            int pid,
            string sessionId,
            int generation,
            int port) =>
            new()
            {
                ["pid"] = pid,
                ["session_id"] = sessionId,
                ["generation"] = generation,
                ["port"] = port,
                ["connector_build"] = ConnectorBuild.Id,
                ["listener_id"] = cache.ListenerId ?? "",
                ["status"] = "stopped",
                ["updated_at_utc"] = DateTime.UtcNow.ToString("o"),
            };

        private static Snapshot LoadUnsafe()
        {
            try
            {
                var path = CachePath;
                if (!File.Exists(path))
                    return null;

                var json = File.ReadAllText(path);
                var data = ConnectorJson.ParseObject(json);
                if (data.Count == 0)
                    return null;

                return new Snapshot
                {
                    Pid = ReadInt(data, "pid"),
                    SessionId = ReadString(data, "session_id"),
                    Generation = ReadInt(data, "generation"),
                    Port = ReadInt(data, "port", ConnectorNetwork.ResolveEditorPort()),
                    ConnectorBuild = ReadInt(data, "connector_build"),
                    ListenerId = ReadString(data, "listener_id"),
                    Status = ReadString(data, "status"),
                    UpdatedAtUtc = ReadString(data, "updated_at_utc"),
                };
            }
            catch
            {
                return null;
            }
        }

        private static void WriteUnsafe(Dictionary<string, object> payload)
        {
            var dir = Path.GetDirectoryName(CachePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var json = ConnectorJson.Serialize(payload);
            File.WriteAllText(CachePath, json);
        }

        private static void ClearUnsafe()
        {
            try
            {
                if (File.Exists(CachePath))
                    File.Delete(CachePath);
            }
            catch
            {
                // ignored
            }
        }

        private static bool ProbeSessionOnPort(int port, string sessionId, int connectorBuild)
        {
            if (string.IsNullOrEmpty(sessionId))
                return false;

            if (!HttpProbe.TryGetHealth("127.0.0.1", port, 800, out var body))
                return false;

            return HttpProbe.TryValidateHealth(body, ConnectorHostKind.Editor, connectorBuild, sessionId);
        }

        private static void WaitForPortRelease(int port, int timeoutMs)
        {
            var deadline = Environment.TickCount + timeoutMs;
            while (Environment.TickCount < deadline)
            {
                if (!IsPortOpen(port))
                    return;

                Thread.Sleep(50);
            }
        }

        private static bool IsPortOpen(int port)
        {
            try
            {
                using var client = new TcpClient();
                var task = client.ConnectAsync("127.0.0.1", port);
                if (!task.Wait(120))
                    return false;
                return client.Connected;
            }
            catch
            {
                return false;
            }
        }

        private static int CurrentPid() => Process.GetCurrentProcess().Id;

        private static bool IsProcessAlive(int pid)
        {
            if (pid <= 0)
                return false;

            try
            {
                var process = Process.GetProcessById(pid);
                process.Dispose();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static int ReadInt(IReadOnlyDictionary<string, object> data, string key, int fallback = 0)
        {
            if (!data.TryGetValue(key, out var value) || value == null)
                return fallback;

            return value switch
            {
                int i => i,
                long l => (int)l,
                string s when int.TryParse(s, out var parsed) => parsed,
                _ => fallback,
            };
        }

        private static string ReadString(IReadOnlyDictionary<string, object> data, string key)
        {
            if (!data.TryGetValue(key, out var value) || value == null)
                return "";
            return value.ToString();
        }
    }
}
