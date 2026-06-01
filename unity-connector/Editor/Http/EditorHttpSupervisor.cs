using System;
using UnityEditor;
using UnityEngine;
using UnityCliConnector.Http;
using UnityCliConnector.Network;

namespace UnityCliConnector
{
    /// <summary>
    /// Keeps a single Editor HTTP listener (:6547): disk cache, session, watchdog.
    /// Listener I/O is delegated to <see cref="ConnectorHttpEndpoint"/>.
    /// </summary>
    [InitializeOnLoad]
    internal static class EditorHttpSupervisor
    {
        private const int HealthTimeoutMs = 1500;
        private const int HealthProbeAttempts = 5;
        private const double WatchdogIntervalSeconds = 2.0;
        private const double WarningCooldownSeconds = 30.0;
        private const double MaxBackoffSeconds = 60.0;
        private const int EnsureRetriesPerBurst = 3;

        private static readonly ConnectorRequestDispatcher Dispatcher = new(
            EditorCommandHost.Instance,
            EditorMainThreadHttp.Instance,
            EditorCommandStore.Instance,
            EditorHttpHealth.Instance);

        private static readonly ConnectorHttpEndpoint Endpoint = new(
            Dispatcher,
            ConnectorNetwork.ResolveEditorPort,
            "Editor HTTP server",
            logLifecycle: false);

        private static string _listenerId;
        private static bool _hooksInstalled;
        private static int _ensurePass;
        private static bool _ensurePending;
        private static int _failureBurst;
        private static double _nextWatchdogUtc;
        private static double _backoffUntilUtc;
        private static double _lastWarningUtc;
        private static readonly object Gate = new();

        static EditorHttpSupervisor()
        {
            EditorHttpSession.BeginDomain();
            InstallHooks();
            ApplyCacheOnDomainStart();
            Stop();
            RequestEnsureRunning(0);
        }

        public static ConnectorListenConfig ListenConfig => Endpoint.ListenConfig;

        public static bool IsServing => Endpoint.IsRunning;

        public static void RequestEnsureRunning(int delayFrames = 1)
        {
            if (_ensurePending || IsInBackoff())
                return;

            _ensurePending = true;

            if (delayFrames <= 0)
            {
                EditorApplication.delayCall += RunEnsurePass;
                return;
            }

            void Chain()
            {
                if (delayFrames <= 1)
                {
                    RunEnsurePass();
                    return;
                }

                delayFrames--;
                EditorApplication.delayCall += Chain;
            }

            EditorApplication.delayCall += Chain;
        }

        public static void Stop()
        {
            lock (Gate)
            {
                StopServerLocked();
            }
        }

        private static void StopServerLocked()
        {
            var wasRunning = Endpoint.IsRunning;
            var port = Endpoint.Port;

            EditorHttpSession.MarkCatalogNotReady();
            Endpoint.Stop(Debug.Log);
            _listenerId = null;

            EditorHttpLocalCache.MarkStopped(
                EditorHttpSession.SessionId,
                EditorHttpSession.Generation,
                port);

            if (wasRunning)
            {
                Debug.Log($"[unity-connector] Editor HTTP server stopped (port {port}).");
            }
        }

        private static void ApplyCacheOnDomainStart()
        {
            var port = ConnectorNetwork.ResolveEditorPort();
            var action = EditorHttpLocalCache.ReconcileOnDomainStart(
                EditorHttpSession.SessionId,
                EditorHttpSession.Generation,
                port);

            switch (action)
            {
                case EditorHttpLocalCache.StartupAction.WaitForPortRelease:
                    EditorHttpLocalCache.PrepareForStart(
                        EditorHttpSession.SessionId,
                        EditorHttpSession.Generation,
                        port);
                    break;

                case EditorHttpLocalCache.StartupAction.ForeignProcessOwnsPort:
                    _backoffUntilUtc = EditorApplication.timeSinceStartup + MaxBackoffSeconds;
                    LogThrottled(
                        $"[unity-connector] Port {port} is already served by another Unity process. " +
                        "Close the other Editor or change UNITY_CMD_PORT.");
                    break;
            }
        }

        private static void InstallHooks()
        {
            if (_hooksInstalled)
                return;
            _hooksInstalled = true;

            EditorApplication.quitting += Stop;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload += OnAfterAssemblyReload;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update += OnEditorUpdate;
        }

        private static void OnBeforeAssemblyReload()
        {
            CommandDiscovery.Invalidate();
            CommandCatalog.ClearCachedVersions();
            Stop();
        }

        private static void OnAfterAssemblyReload()
        {
            _failureBurst = 0;
            _backoffUntilUtc = 0;
            ApplyCacheOnDomainStart();
            RequestEnsureRunning(1);
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state is PlayModeStateChange.EnteredPlayMode or PlayModeStateChange.EnteredEditMode)
            {
                _failureBurst = 0;
                _backoffUntilUtc = 0;
                RequestEnsureRunning(1);
            }
        }

        private static void OnEditorUpdate()
        {
            var now = EditorApplication.timeSinceStartup;
            if (now < _nextWatchdogUtc)
                return;

            _nextWatchdogUtc = now + WatchdogIntervalSeconds;

            if (IsInBackoff())
                return;

            if (IsServing
                && ProbeOwnHealth(out _)
                && EditorHttpLocalCache.MatchesRunningListener(
                    EditorHttpSession.SessionId,
                    Endpoint.Port,
                    _listenerId))
            {
                _failureBurst = 0;
                return;
            }

            RequestEnsureRunning(0);
        }

        private static bool IsInBackoff() =>
            EditorApplication.timeSinceStartup < _backoffUntilUtc;

        private static void RunEnsurePass()
        {
            _ensurePending = false;
            if (IsInBackoff())
                return;

            _ensurePass++;
            EnsureRunningLocked();

            if (IsServing && ProbeOwnHealth(out _))
            {
                _ensurePass = 0;
                _failureBurst = 0;
                _backoffUntilUtc = 0;
                return;
            }

            if (_ensurePass < EnsureRetriesPerBurst)
            {
                RequestEnsureRunning(2);
                return;
            }

            _ensurePass = 0;
            RegisterFailureBurst();
        }

        private static void RegisterFailureBurst()
        {
            _failureBurst++;
            var backoff = Math.Min(MaxBackoffSeconds, 5.0 * _failureBurst);
            _backoffUntilUtc = EditorApplication.timeSinceStartup + backoff;
            LogThrottled(
                $"[unity-connector] Editor HTTP unavailable; backing off {backoff:0}s " +
                $"(burst {_failureBurst}). See ~/.unity-cmd/editor-http.json or restart the Editor.");
        }

        private static void EnsureRunningLocked()
        {
            lock (Gate)
            {
                var port = ConnectorNetwork.ResolveEditorPort();

                if (IsServing
                    && ProbeOwnHealth(out _)
                    && EditorHttpLocalCache.MatchesRunningListener(
                        EditorHttpSession.SessionId,
                        port,
                        _listenerId))
                {
                    ScheduleCatalogWarmup();
                    return;
                }

                var prepare = EditorHttpLocalCache.PrepareForStart(
                    EditorHttpSession.SessionId,
                    EditorHttpSession.Generation,
                    port);

                if (prepare == EditorHttpLocalCache.PrepareResult.PortOwnedByOtherProcess)
                {
                    _backoffUntilUtc = EditorApplication.timeSinceStartup + MaxBackoffSeconds;
                    LogThrottled(
                        $"[unity-connector] Port {port} is owned by another Unity instance (see ~/.unity-cmd/editor-http.json).");
                    return;
                }

                if (Endpoint.IsRunning)
                    StopServerLocked();

                if (!Endpoint.TryStart(Debug.Log, LogThrottled))
                {
                    EditorHttpLocalCache.MarkStopped(
                        EditorHttpSession.SessionId,
                        EditorHttpSession.Generation,
                        port);
                    return;
                }

                if (!ProbeOwnHealth(out var healthError))
                {
                    LogThrottled(
                        "[unity-connector] Editor HTTP started but health check failed; will retry later."
                        + (string.IsNullOrEmpty(healthError) ? "" : $" ({healthError})"));
                    StopServerLocked();
                    return;
                }

                _listenerId = Guid.NewGuid().ToString("N");
                EditorHttpLocalCache.MarkRunning(
                    EditorHttpSession.SessionId,
                    EditorHttpSession.Generation,
                    port,
                    _listenerId);

                Debug.Log(
                    $"[unity-connector] Editor HTTP server started (port {port}, host editor, build {ConnectorBuild.Id}).");

                ScheduleCatalogWarmup();
            }
        }

        private static void ScheduleCatalogWarmup()
        {
            if (EditorHttpSession.CatalogReady)
                return;

            EditorApplication.delayCall += static () =>
            {
                try
                {
                    CommandCatalog.BuildResponse(EditorCommandHost.Instance.HostName);
                    EditorHttpSession.MarkCatalogReady();
                }
                catch (Exception ex)
                {
                    EditorHttpSession.MarkCatalogNotReady();
                    LogThrottled($"[unity-connector] Catalog warmup failed: {ex.Message}");
                }
            };
        }

        private static bool ProbeOwnHealth(out string error) =>
            Endpoint.TryProbeHealth(
                EditorCommandHost.Instance.HostName,
                ConnectorBuild.Id,
                EditorHttpSession.SessionId,
                HealthTimeoutMs,
                HealthProbeAttempts,
                out error);

        private static void LogThrottled(string message)
        {
            var now = EditorApplication.timeSinceStartup;
            if (now - _lastWarningUtc < WarningCooldownSeconds)
                return;

            _lastWarningUtc = now;
            Debug.LogWarning(message);
        }
    }
}
