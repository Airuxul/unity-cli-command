using System;
using UnityEditor;
using UnityEngine;
using UnityCliConnector.Http;
using UnityCliConnector.Network;

namespace UnityCliConnector
{
    [InitializeOnLoad]
    public static class EditorHttpHost
    {
        private static HttpServer _server;
        private static ConnectorListenConfig _listen;
        private static bool _hooksInstalled;
        private static bool _startScheduled;
        private static readonly object Gate = new();
        private static readonly ConnectorRequestDispatcher Dispatcher = new(
            EditorCommandHost.Instance,
            EditorCommandBridge.Instance,
            EditorCommandStore.Instance);

        static EditorHttpHost()
        {
            InstallLifecycleHooks();
            Stop();
            ScheduleStart();
        }

        private static void InstallLifecycleHooks()
        {
            if (_hooksInstalled)
                return;
            _hooksInstalled = true;

            EditorApplication.quitting += Stop;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        public static ConnectorListenConfig ListenConfig => _listen;

        public static void Start()
        {
            lock (Gate)
            {
                // Editor host should not bind while in Play Mode.
                if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
                    return;

                ConnectorHttpLifecycle.TryStart(
                    ref _server,
                    ref _listen,
                    Dispatcher,
                    ConnectorNetwork.ResolveEditorPort(),
                    "Editor HTTP",
                    Debug.Log,
                    Debug.LogError);
            }
        }

        private static void ScheduleStart()
        {
            if (_startScheduled)
                return;
            _startScheduled = true;
            EditorApplication.delayCall += DelayedStart;
        }

        private static void DelayedStart()
        {
            _startScheduled = false;
            EditorApplication.delayCall -= DelayedStart;
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            Start();
        }

        public static void Stop()
        {
            lock (Gate)
            {
                ConnectorHttpLifecycle.Stop(ref _server);
                _listen = null;
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.EnteredPlayMode)
            {
                Stop();
                return;
            }

            if (state == PlayModeStateChange.EnteredEditMode)
            {
                Stop();
                ScheduleStart();
            }
        }
    }
}
