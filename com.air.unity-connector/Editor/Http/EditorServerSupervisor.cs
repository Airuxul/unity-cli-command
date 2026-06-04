using System;
using Air.UnityConnector.Host;
using UnityEditor;
using UnityEngine;

namespace Air.UnityConnector.Server
{
    /// <summary>Editor HTTP supervisor phase (internal FSM). Distinct from connector pipeline strings on /health.</summary>
    internal enum EditorServerSupervisorPhase
    {
        Stopped,
        Draining,
        Starting,
        Running,
        BackoffForeign,
    }

    /// <summary>
    /// Single writer for Editor HTTP listener lifecycle (phase FSM; game-core StateMachine types avoided due to <c>Air.UnityConnector.State</c> namespace clash).
    /// <see cref="EditorConnectorBootstrap"/> forwards Unity events here only.
    /// </summary>
    internal sealed class EditorServerSupervisor
    {
        private static EditorServerSupervisor _instance;

        private const double WatchdogIntervalSeconds = 2.0;
        private const double WarningCooldownSeconds = 30.0;
        private const double PlayTransitionSettleSeconds = 4.0;
        private const int EnsureRetriesPerBurst = 3;

        private readonly object _gate = new();
        private int _ensurePass;
        private int _failureBurst;
        private double _nextWatchdogUtc;
        private double _backoffUntilUtc;
        private double _transitionUntilUtc;
        private double _lastWarningUtc;
        private double _startingSinceUtc;
        private const double StartingStuckSeconds = 15.0;

        public static EditorServerSupervisor Instance => _instance ??= new EditorServerSupervisor();

        public EditorServerSupervisorPhase Phase { get; private set; } = EditorServerSupervisorPhase.Stopped;

        internal int FailureBurst => _failureBurst;
        internal int EnsurePass => _ensurePass;

        public static void RequestEnsureRunning(int delayFrames = 1) =>
            Instance.RequestStart(delayFrames);

        public void RequestStart(int delayFrames = 1)
        {
            if (delayFrames <= 0)
            {
                EnqueueStart(immediate: true);
                return;
            }

            void Chain()
            {
                if (delayFrames <= 1)
                {
                    EnqueueStart(immediate: true);
                    return;
                }

                delayFrames--;
                EditorApplication.delayCall += Chain;
            }

            EditorApplication.delayCall += Chain;
        }

        public void RequestDrain()
        {
            lock (_gate)
            {
                if (Phase == EditorServerSupervisorPhase.Draining)
                    return;

                EnterDraining("RequestDrain", thenStart: false);
            }
        }

        /// <summary>Controlled restart for CLI (P3): drain then start.</summary>
        public void RequestControlledRestart()
        {
            lock (_gate)
            {
                ResetTransientBackoff();
                EnterDraining("RequestControlledRestart", thenStart: true);
            }
        }

        internal void ResetTransientBackoff()
        {
            _failureBurst = 0;
            _backoffUntilUtc = 0;
            _ensurePass = 0;
        }

        internal void OnAfterDomainReload() => ResetTransientBackoff();

        internal void HandleDomainStart()
        {
            var action = EditorServerLifecycle.ApplyCacheOnDomainStart();
            if (action == EditorHttpLocalCache.StartupAction.ForeignProcessOwnsPort)
            {
                _backoffUntilUtc = EditorApplication.timeSinceStartup + EditorServerLifecycle.ForeignPortBackoffSeconds;
                LogThrottled(
                    $"[unity-connector] Port {HostNetwork.ResolveEditorPort()} is already served by another Unity process. " +
                    "Close the other Editor or change UNITY_CMD_PORT.");
                if (Phase != EditorServerSupervisorPhase.BackoffForeign)
                    EnterBackoffForeign();
            }
        }

        internal void OnWatchdog()
        {
            var now = EditorApplication.timeSinceStartup;
            if (now < _nextWatchdogUtc)
                return;

            _nextWatchdogUtc = now + WatchdogIntervalSeconds;

            if (IsInBackoff() || IsHttpTransitionUnstable())
                return;

            if (EditorConnectorServer.Instance.TryDescribeRunningCache(out var watchdogCacheReason))
            {
                if (Phase == EditorServerSupervisorPhase.Running)
                {
                    EditorServerLifecycle.ReconcileRunningCache("watchdog", "OnWatchdog");
                    _failureBurst = 0;
                    return;
                }

                if (Phase == EditorServerSupervisorPhase.Stopped)
                    EnterRunning(reconcileOnly: true);
                return;
            }

            if (Phase == EditorServerSupervisorPhase.Draining)
                return;

            if (Phase == EditorServerSupervisorPhase.Starting
                && EditorApplication.timeSinceStartup - _startingSinceUtc > StartingStuckSeconds)
            {
                EditorConnectorStartupLog.Record(
                    "OnWatchdog",
                    $"Starting phase stuck >{StartingStuckSeconds:0}s — resetting to Stopped");
                EnterStopped("OnWatchdog:starting_stuck");
                RequestStart(0);
                return;
            }

            if (Phase == EditorServerSupervisorPhase.Starting)
                return;

            if (!EditorConnectorServer.Instance.TryDescribeRunningCache(out var notReadyReason))
                EditorServerDiagnostics.Decision(
                    "OnWatchdog",
                    $"RequestStart cache_miss:{notReadyReason} burst={_failureBurst} pass={_ensurePass}");

            RequestStart(0);
        }

        internal void OnStartSequenceFinished(EditorServerLifecycle.StartAttemptResult result)
        {
            lock (_gate)
            {
                switch (result)
                {
                    case EditorServerLifecycle.StartAttemptResult.Running:
                    case EditorServerLifecycle.StartAttemptResult.CacheReconciled:
                        _ensurePass = 0;
                        _failureBurst = 0;
                        _backoffUntilUtc = 0;
                        EnterRunning(reconcileOnly: false);
                        break;

                    case EditorServerLifecycle.StartAttemptResult.ForeignPort:
                        _backoffUntilUtc = EditorApplication.timeSinceStartup + EditorServerLifecycle.MaxBackoffSeconds;
                        EnterBackoffForeign();
                        break;

                    case EditorServerLifecycle.StartAttemptResult.Failed:
                        HandleStartFailure();
                        break;
                }
            }
        }

        internal static void LogThrottled(string message)
        {
            var supervisor = Instance;
            var now = EditorApplication.timeSinceStartup;
            if (now - supervisor._lastWarningUtc < WarningCooldownSeconds)
                return;

            supervisor._lastWarningUtc = now;
            Debug.LogWarning(message);
        }

        internal static void LogConnectorError(string message) =>
            EditorConnectorStartupLog.Record("LogConnectorError", message);

        internal void MarkPlayTransition() =>
            _transitionUntilUtc = EditorApplication.timeSinceStartup + PlayTransitionSettleSeconds;

        /// <summary>After play enter/exit: reconcile if already listening; otherwise defer start until transition settles.</summary>
        internal void OnPlayModeSettled()
        {
            MarkPlayTransition();
            EditorServerDiagnostics.Trace("OnPlayModeSettled", "entered");

            if (EditorConnectorServer.Instance.IsListening)
            {
                lock (_gate)
                {
                    if (EditorConnectorServer.Instance.TryDescribeRunningCache(out var reason))
                    {
                        EditorServerDiagnostics.Decision("OnPlayModeSettled", $"reconcile:{reason}");
                        EditorServerLifecycle.ReconcileRunningCache(reason, "OnPlayModeSettled");
                        EnterRunning(reconcileOnly: true);
                        return;
                    }
                }

                EditorServerDiagnostics.Decision("OnPlayModeSettled", "defer:listener_up_cache_miss");
                RequestStart(5);
                return;
            }

            EditorServerDiagnostics.Decision("OnPlayModeSettled", "defer:listener_down");
            RequestStart(8);
        }

        internal bool IsHttpTransitionUnstable() =>
            EditorApplication.timeSinceStartup < _transitionUntilUtc
            || EditorPlayState.IsCompiling
            || EditorPlayState.IsUpdating;

        internal bool IsInBackoff() => EditorApplication.timeSinceStartup < _backoffUntilUtc;

        private void EnqueueStart(bool immediate)
        {
            if (IsInBackoff())
            {
                EditorServerDiagnostics.Decision("EnqueueStart", "skip:in_backoff");
                return;
            }

            if (immediate)
                _nextWatchdogUtc = 0;

            if (ShouldDeferStart())
            {
                EditorServerDiagnostics.Decision("EnqueueStart", "defer:transition_unstable");
                RequestStart(5);
                return;
            }

            lock (_gate)
            {
                if (Phase == EditorServerSupervisorPhase.Draining)
                {
                    EditorServerDiagnostics.Decision("EnqueueStart", "skip:draining");
                    return;
                }

                if (Phase == EditorServerSupervisorPhase.Starting)
                {
                    EditorServerDiagnostics.Decision("EnqueueStart", "skip:starting");
                    return;
                }

                if (Phase == EditorServerSupervisorPhase.Running)
                {
                    if (EditorConnectorServer.Instance.TryDescribeRunningCache(out var runningReason))
                    {
                        EditorServerDiagnostics.Decision("EnqueueStart", $"running:reconcile({runningReason})");
                        EditorServerLifecycle.ReconcileRunningCache(runningReason, "RequestStart(running)");
                        return;
                    }

                    if (EditorConnectorServer.Instance.IsListening)
                    {
                        if (ShouldDeferStart())
                        {
                            EditorServerDiagnostics.Decision("EnqueueStart", "running:defer_reuse_listener");
                            RequestStart(5);
                            return;
                        }

                        EditorServerDiagnostics.Decision("EnqueueStart", "running:EnterStarting(reuse_listener)");
                        EnterStarting();
                        return;
                    }

                    EditorServerDiagnostics.Decision("EnqueueStart", "running:EnterDraining(not_listening)");
                    EnterDraining("RequestStart(running)", thenStart: true);
                    return;
                }

                if (Phase == EditorServerSupervisorPhase.BackoffForeign)
                {
                    if (IsInBackoff())
                    {
                        EditorServerDiagnostics.Decision("EnqueueStart", "skip:backoff_foreign");
                        return;
                    }

                    SetPhase(EditorServerSupervisorPhase.Stopped, "EnqueueStart:foreign_expired");
                }

                if (EditorConnectorServer.Instance.TryDescribeRunningCache(out var cacheReason))
                {
                    EditorServerDiagnostics.Decision("EnqueueStart", $"cache_hit:{cacheReason}");
                    EditorServerLifecycle.ReconcileRunningCache(cacheReason, "RequestStart(cache_hit)");
                    EnterRunning(reconcileOnly: true);
                    return;
                }

                EditorServerDiagnostics.Decision("EnqueueStart", "EnterStarting");
                EnterStarting();
            }
        }

        private bool ShouldDeferStart() => IsHttpTransitionUnstable();

        private void HandleStartFailure()
        {
            if (IsHttpTransitionUnstable())
            {
                EditorServerDiagnostics.Trace("HandleStartFailure", "defer:no_log_during_transition");
                SetPhase(EditorServerSupervisorPhase.Stopped, "HandleStartFailure:transitional");
                RequestStart(5);
                return;
            }

            _ensurePass++;
            EditorServerDiagnostics.Trace(
                "HandleStartFailure",
                $"attempt {_ensurePass}/{EnsureRetriesPerBurst} burst={_failureBurst}");
            EditorConnectorStartupLog.Record(
                "HandleStartFailure",
                $"start attempt {_ensurePass}/{EnsureRetriesPerBurst} failed (phase={Phase}) | {EditorServerDiagnostics.CaptureSnapshot()}");

            if (_ensurePass < EnsureRetriesPerBurst)
            {
                EnterStopped("HandleStartFailure:retry");
                RequestStart(2);
                return;
            }

            _ensurePass = 0;
            RegisterFailureBurst();
            EnterStopped("HandleStartFailure:burst");
        }

        private void RegisterFailureBurst()
        {
            _failureBurst++;
            var backoff = IsHttpTransitionUnstable()
                ? Math.Min(3.0, 1.0 + _failureBurst)
                : Math.Min(EditorServerLifecycle.MaxBackoffSeconds, 5.0 * _failureBurst);
            _backoffUntilUtc = EditorApplication.timeSinceStartup + backoff;

            var message =
                $"[unity-connector] Editor HTTP unavailable; backing off {backoff:0}s " +
                $"(burst {_failureBurst}). See ~/.unity-cmd/editor-http.json or restart the Editor.";

            EditorConnectorStartupLog.Record("RegisterFailureBurst", message);
        }

        private void SetPhase(EditorServerSupervisorPhase next, string site)
        {
            if (Phase == next)
                return;

            EditorServerDiagnostics.Phase(Phase, next, site);
            Phase = next;
        }

        private void EnterStopped(string site = "EnterStopped") =>
            SetPhase(EditorServerSupervisorPhase.Stopped, site);

        private void EnterDraining(string site, bool thenStart)
        {
            SetPhase(EditorServerSupervisorPhase.Draining, site);
            EditorServerLifecycle.PerformStop(site);
            EnterStopped(site + ":after_stop");

            if (!thenStart)
                return;

            EditorApplication.delayCall += () => RequestStart(2);
        }

        private void EnterStarting()
        {
            SetPhase(EditorServerSupervisorPhase.Starting, "EnterStarting");
            _startingSinceUtc = EditorApplication.timeSinceStartup;
            // Run synchronously: delayCall may not run during domain reload / script compile.
            RunStartingSequence();
        }

        private void RunStartingSequence()
        {
            if (Phase != EditorServerSupervisorPhase.Starting)
                return;

            try
            {
                var result = EditorServerLifecycle.TryStartListening();
                OnStartSequenceFinished(result);
            }
            catch (Exception ex)
            {
                EditorConnectorStartupLog.Record("TryStartListening(exception)", ex.ToString());
                lock (_gate)
                {
                    if (Phase == EditorServerSupervisorPhase.Starting)
                        HandleStartFailure();
                }
            }
        }

        private void EnterRunning(bool reconcileOnly)
        {
            SetPhase(EditorServerSupervisorPhase.Running, reconcileOnly ? "EnterRunning:reconcile" : "EnterRunning");
            if (reconcileOnly)
                return;

            EditorServerLifecycle.WarmCatalogIfNeeded();
        }

        private void EnterBackoffForeign()
        {
            SetPhase(EditorServerSupervisorPhase.BackoffForeign, "EnterBackoffForeign");
            ScheduleBackoffRetry();
        }

        private void ScheduleBackoffRetry()
        {
            EditorApplication.delayCall += () =>
            {
                if (Phase != EditorServerSupervisorPhase.BackoffForeign)
                    return;

                if (IsInBackoff())
                {
                    ScheduleBackoffRetry();
                    return;
                }

                lock (_gate)
                {
                    EnterStopped("ScheduleBackoffRetry");
                    RequestStart(0);
                }
            };
        }
    }
}
