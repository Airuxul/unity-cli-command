using System;
using System.Collections.Concurrent;
using System.Threading;
using UnityEditor;

namespace UnityCliConnector
{
    [InitializeOnLoad]
    internal static class EditorMainThread
    {
        private sealed class WorkItem
        {
            public Func<object> Func;
            public Action<object> SetResult;
            public Action<Exception> SetError;
            public ManualResetEventSlim Done = new ManualResetEventSlim(false);
        }

        private static readonly ConcurrentQueue<WorkItem> Queue = new();

        static EditorMainThread()
        {
            EditorApplication.update += DrainQueue;
        }

        public static T Run<T>(Func<T> func, TimeSpan timeout)
        {
            if (MainThread.IsCurrent)
                return func();

            object result = null;
            Exception error = null;
            var item = new WorkItem
            {
                Func = () => func(),
                SetResult = r => result = r,
                SetError = e => error = e,
            };

            Queue.Enqueue(item);

            if (!item.Done.Wait(timeout))
                throw new TimeoutException("Timed out waiting for Unity main thread.");

            if (error != null)
                throw error;

            return (T)result;
        }

        private static void DrainQueue()
        {
            while (Queue.TryDequeue(out var item))
            {
                try
                {
                    var r = item.Func();
                    item.SetResult(r);
                }
                catch (Exception ex)
                {
                    item.SetError(ex);
                }
                finally
                {
                    item.Done.Set();
                }
            }
        }
    }
}
