using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEditor;
using UnityCliConnector.Http;

namespace UnityCliConnector
{
    /// <summary>All Editor HTTP handlers that touch Unity APIs run on the Editor main thread.</summary>
    [InitializeOnLoad]
    internal sealed class EditorMainThreadHttp : IMainThreadHttpScheduler
    {
        public static readonly EditorMainThreadHttp Instance = new();

        private readonly ConcurrentQueue<MainThreadHttpWork.Item> _queue = new();

        static EditorMainThreadHttp()
        {
            EditorApplication.update += static () => Instance.Drain();
        }

        public void Schedule(string body, Action<int, Dictionary<string, object>> writeJson) =>
            _queue.Enqueue(new MainThreadHttpWork.Item
            {
                Kind = MainThreadHttpWork.Kind.Command,
                Body = body,
                WriteJson = writeJson,
            });

        public void ScheduleCatalog(Action<int, Dictionary<string, object>> writeJson) =>
            _queue.Enqueue(new MainThreadHttpWork.Item
            {
                Kind = MainThreadHttpWork.Kind.Catalog,
                WriteJson = writeJson,
            });

        public void ScheduleCommandStatus(string commandId, Action<int, Dictionary<string, object>> writeJson) =>
            _queue.Enqueue(new MainThreadHttpWork.Item
            {
                Kind = MainThreadHttpWork.Kind.CommandStatus,
                CommandId = commandId,
                WriteJson = writeJson,
            });

        private void Drain()
        {
            while (_queue.TryDequeue(out var item))
            {
                MainThreadHttpWork.Process(
                    item,
                    EditorCommandHost.Instance,
                    EditorCommandStore.Instance.GetCommandResponse,
                    EditorHttpSession.MarkCatalogReady);
            }
        }
    }
}
