using UnityEditor;
using UnityEditor.Compilation;
using Air.UnityConnector.Cli;
using Air.UnityConnector.Invoke;
using Air.UnityConnector.Server;

namespace Air.UnityConnector
{
    /// <summary>
    /// Forwards Unity lifecycle events to <see cref="EditorServerSupervisor"/> (single HTTP write path).
    /// </summary>
    [InitializeOnLoad]
    internal static class EditorConnectorBootstrap
    {
        private static bool _hooksInstalled;

        static EditorConnectorBootstrap()
        {
            ConnectorSerialization.EnsureRegistered();
            EditorHttpSession.BeginDomain();
            InstallHooks();
            EditorServerSupervisor.Instance.HandleDomainStart();
            EditorServerLifecycle.PerformStop("domain_start");
            EditorServerSupervisor.RequestEnsureRunning(0);
            EditorApplication.update += static () => EditorConnectorServer.Instance.Scheduler.Drain();
        }

        public static void RequestEnsureRunning(int delayFrames = 1) =>
            EditorServerSupervisor.RequestEnsureRunning(delayFrames);

        public static void Stop() => EditorServerSupervisor.Instance.RequestDrain();

        internal static void LogThrottled(string message) =>
            EditorServerSupervisor.LogThrottled(message);

        internal static void LogConnectorError(string message) =>
            EditorServerSupervisor.LogConnectorError(message);

        internal static bool IsHttpTransitionUnstable() =>
            EditorServerSupervisor.Instance.IsHttpTransitionUnstable();

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
            CompilationPipeline.compilationFinished += OnCompilationFinished;
        }

        private static void OnCompilationFinished(object _)
        {
            EditorServerSupervisor.Instance.ResetTransientBackoff();
            EditorServerSupervisor.RequestEnsureRunning(4);
        }

        private static void OnBeforeAssemblyReload()
        {
            EditorServerDiagnostics.Trace("OnBeforeAssemblyReload", null);
            CliCommandDiscovery.Invalidate();
            InvokeCatalog.ClearCachedVersions();
            EditorJobStateManager.FlushToLedger();
            EditorInstanceFile.MarkReloading();
            EditorServerSupervisor.Instance.RequestDrain();
        }

        private static void OnAfterAssemblyReload()
        {
            EditorHttpSession.SetDomainReloading(true, "OnAfterAssemblyReload");
            EditorInstanceFile.MarkReloading();
            EditorJobStateManager.Reload();
            EditorServerSupervisor.Instance.OnAfterDomainReload();
            EditorServerSupervisor.Instance.HandleDomainStart();
            EditorServerSupervisor.RequestEnsureRunning(0);
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            EditorServerDiagnostics.Trace("playModeStateChanged", state.ToString());
            switch (state)
            {
                case PlayModeStateChange.ExitingEditMode:
                case PlayModeStateChange.ExitingPlayMode:
                    EditorServerSupervisor.Instance.MarkPlayTransition();
                    return;

                case PlayModeStateChange.EnteredPlayMode:
                case PlayModeStateChange.EnteredEditMode:
                    EditorServerSupervisor.Instance.OnPlayModeSettled();
                    return;
            }
        }

        private static void OnEditorUpdate() =>
            EditorServerSupervisor.Instance.OnWatchdog();
    }
}
