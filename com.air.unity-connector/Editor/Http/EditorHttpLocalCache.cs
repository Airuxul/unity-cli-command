using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using Air.UnityConnector.Http;
using Air.UnityConnector.Host;
using Air.UnityGameCore.Runtime.Serialization;

namespace Air.UnityConnector
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
            public string Phase;
            public string LastError;
            public string ProjectPath;
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

        /// <summary>Wait until loopback port is free (call after Stop, before TryStart).</summary>
        public static void WaitForPortAvailable(int port, int timeoutMs = 3000)
        {
            lock (FileGate)
            {
                WaitForPortRelease(port, timeoutMs);
            }
        }

        public static void MarkRunning(
            string sessionId,
            int generation,
            int port,
            string listenerId,
            Server.EditorServerSupervisorPhase? supervisorPhase = null,
            string lastError = null) =>
            WriteStatus(sessionId, generation, port, listenerId, "running", supervisorPhase, lastError);

        public static void MarkStopped(
            string sessionId,
            int generation,
            int port,
            Server.EditorServerSupervisorPhase? supervisorPhase = null,
            string lastError = null) =>
            WriteStatus(sessionId, generation, port, "", "stopped", supervisorPhase, lastError);

        private static void WriteStatus(
            string sessionId,
            int generation,
            int port,
            string listenerId,
            string status,
            Server.EditorServerSupervisorPhase? supervisorPhase,
            string lastError)
        {
            lock (FileGate)
            {
                var cache = LoadUnsafe();
                var payload = status == "stopped" && cache != null
                    ? MakeStopped(cache, CurrentPid(), sessionId, generation, port)
                    : new Dictionary<string, object>
                    {
                        ["pid"] = CurrentPid(),
                        ["session_id"] = sessionId,
                        ["generation"] = generation,
                        ["port"] = port,
                        ["connector_build"] = ConnectorBuild.Id,
                        ["listener_id"] = listenerId ?? "",
                        ["status"] = status,
                        ["updated_at_utc"] = DateTime.UtcNow.ToString("o"),
                    };

                if (supervisorPhase.HasValue)
                    payload["phase"] = supervisorPhase.Value.ToString();
                if (!string.IsNullOrEmpty(lastError))
                    payload["last_error"] = lastError;

                var dataPath = UnityEngine.Application.dataPath;
                if (!string.IsNullOrEmpty(dataPath))
                    payload["project_path"] = Path.GetDirectoryName(dataPath) ?? "";

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

        internal static string DescribeForDiagnostics()
        {
            lock (FileGate)
            {
                var cache = LoadUnsafe();
                if (cache == null)
                    return "cache=null";

                var lid = string.IsNullOrEmpty(cache.ListenerId) ? "-" : cache.ListenerId.Substring(0, Math.Min(8, cache.ListenerId.Length));
                return $"status={cache.Status ?? "?"} pid={cache.Pid} lid={lid} phase={cache.Phase ?? "-"}";
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
                ConnectorSerialization.EnsureRegistered();
                var data = JsonSerialization.ParseObject(json);
                if (data.Count == 0)
                    return null;

                return new Snapshot
                {
                    Pid = ReadInt(data, "pid"),
                    SessionId = ReadString(data, "session_id"),
                    Generation = ReadInt(data, "generation"),
                    Port = ReadInt(data, "port", HostNetwork.ResolveEditorPort()),
                    ConnectorBuild = ReadInt(data, "connector_build"),
                    ListenerId = ReadString(data, "listener_id"),
                    Status = ReadString(data, "status"),
                    UpdatedAtUtc = ReadString(data, "updated_at_utc"),
                    Phase = ReadString(data, "phase"),
                    LastError = ReadString(data, "last_error"),
                    ProjectPath = ReadString(data, "project_path"),
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

            ConnectorSerialization.EnsureRegistered();
            var json = JsonSerialization.Serialize(payload);
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

            return HttpProbe.TryValidateHealth(body, HostKind.Editor, connectorBuild, sessionId);
        }

        private static void WaitForPortRelease(int port, int timeoutMs) =>
            PortReachability.WaitUntilFree("127.0.0.1", port, timeoutMs);

        private static bool IsPortOpen(int port) =>
            PortReachability.IsPortOpen("127.0.0.1", port);

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
