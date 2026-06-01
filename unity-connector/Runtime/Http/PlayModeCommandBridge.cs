using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityCliConnector.Http;
using UnityEngine;

namespace UnityCliConnector
{
    /// <summary>Runs HTTP command work on the play-mode main thread.</summary>
    public sealed class PlayModeCommandBridge : IMainThreadHttpScheduler
    {
        private readonly ICommandHost _host;
        private readonly RuntimeCommandStore _commands;
        private readonly ConcurrentQueue<MainThreadHttpWork.Item> _queue = new();
        private GameObject _driver;

        public PlayModeCommandBridge(ICommandHost host)
        {
            _host = host;
            _commands = new RuntimeCommandStore(host.HostName);
        }

        public void EnsureStarted()
        {
            if (_driver != null)
                return;

            CleanupStaleBridgeObjects();

            _driver = new GameObject($"UnityCliConnector.Bridge.{_host.HostName}")
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            UnityEngine.Object.DontDestroyOnLoad(_driver);
            _driver.AddComponent<BridgeDriver>().Init(this);
        }

        public void Schedule(string body, Action<int, Dictionary<string, object>> writeJson)
        {
            EnsureStarted();
            _queue.Enqueue(new MainThreadHttpWork.Item
            {
                Kind = MainThreadHttpWork.Kind.Command,
                Body = body,
                WriteJson = writeJson,
            });
        }

        public void ScheduleCatalog(Action<int, Dictionary<string, object>> writeJson)
        {
            EnsureStarted();
            _queue.Enqueue(new MainThreadHttpWork.Item
            {
                Kind = MainThreadHttpWork.Kind.Catalog,
                WriteJson = writeJson,
            });
        }

        public void ScheduleCommandStatus(string commandId, Action<int, Dictionary<string, object>> writeJson)
        {
            EnsureStarted();
            _queue.Enqueue(new MainThreadHttpWork.Item
            {
                Kind = MainThreadHttpWork.Kind.CommandStatus,
                CommandId = commandId,
                WriteJson = writeJson,
            });
        }

        private void Drain()
        {
            RuntimeCommandStateManager.Tick(_host.HostName);

            while (_queue.TryDequeue(out var item))
            {
                MainThreadHttpWork.Process(
                    item,
                    _host,
                    _commands.GetCommandResponse);
            }
        }

        private static void CleanupStaleBridgeObjects()
        {
            var allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
            foreach (var go in allObjects)
            {
                if (go == null || string.IsNullOrEmpty(go.name))
                    continue;
                if (!go.name.StartsWith("UnityCliConnector.Bridge.", StringComparison.Ordinal))
                    continue;

                var components = go.GetComponents<Component>();
                var hasMissingScript = false;
                foreach (var component in components)
                {
                    if (component == null)
                    {
                        hasMissingScript = true;
                        break;
                    }
                }

                if (!hasMissingScript)
                    continue;

                if (Application.isPlaying)
                    UnityEngine.Object.Destroy(go);
                else
                    UnityEngine.Object.DestroyImmediate(go);
            }
        }

        private sealed class BridgeDriver : MonoBehaviour
        {
            private PlayModeCommandBridge _bridge;

            public void Init(PlayModeCommandBridge bridge) => _bridge = bridge;

            private void Update() => _bridge?.Drain();
        }
    }
}
