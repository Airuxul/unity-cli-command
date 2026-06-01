using UnityCliConnector.Http;
using UnityCliConnector.Network;

namespace UnityCliConnector
{
    /// <summary>Compatibility entry for Editor HTTP; lifecycle is owned by <see cref="EditorHttpSupervisor"/>.</summary>
    public static class EditorHttpHost
    {
        public static ConnectorListenConfig ListenConfig => EditorHttpSupervisor.ListenConfig;

        public static void Start() => EditorHttpSupervisor.RequestEnsureRunning(0);

        public static void Stop() => EditorHttpSupervisor.Stop();
    }
}
