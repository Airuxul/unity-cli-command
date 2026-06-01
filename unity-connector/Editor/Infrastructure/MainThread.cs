using System.Threading;
using UnityEditor;

namespace UnityCliConnector
{
    [InitializeOnLoad]
    internal static class MainThread
    {
        public static readonly int Id;

        static MainThread()
        {
            Id = Thread.CurrentThread.ManagedThreadId;
        }

        public static bool IsCurrent => Thread.CurrentThread.ManagedThreadId == Id;
    }
}
