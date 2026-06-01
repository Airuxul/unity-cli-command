using UnityEngine;
using UnityCliConnector.Http;
using UnityCliConnector.Network;

namespace UnityCliConnector
{
    /// <summary>HTTP for Unity Editor Play Mode (<c>host=editor_play</c>, port 6794).</summary>
    public static class EditorPlayHttpHost
    {
        private static readonly PlayModeHttpStack Stack = new(
            ConnectorHostKind.EditorPlay,
            "Editor Play HTTP server",
            ConnectorNetwork.ResolveEditorPlayPort);

        public static bool IsRunning => Stack.Endpoint.IsRunning;

        public static void Start() => Stack.Start();

        public static void Stop() => Stack.Stop();
    }

    /// <summary>HTTP for Development Build players (<c>host=player</c>, port 6795).</summary>
    public static class PlayerHttpHost
    {
        private static readonly PlayModeHttpStack Stack = new(
            ConnectorHostKind.Player,
            "Player HTTP server",
            ConnectorNetwork.ResolvePlayerPort);

        public static bool IsRunning => Stack.Endpoint.IsRunning;

        public static void Start() => Stack.Start();

        public static void Stop() => Stack.Stop();
    }

    /// <summary>Command host + main-thread bridge + shared HTTP endpoint for one play-mode listener.</summary>
    internal sealed class PlayModeHttpStack
    {
        private readonly PlayModeCommandBridge _bridge;
        public ConnectorHttpEndpoint Endpoint { get; }

        public PlayModeHttpStack(string hostName, string label, System.Func<int> resolvePort)
        {
            var host = new PlayModeCommandHost(hostName);
            _bridge = new PlayModeCommandBridge(host);
            Endpoint = new ConnectorHttpEndpoint(
                new ConnectorRequestDispatcher(host, _bridge, commands: null),
                resolvePort,
                label,
                onStarted: () => _bridge.EnsureStarted());
        }

        public void Start() =>
            Endpoint.TryStart(Debug.Log, Debug.LogWarning);

        public void Stop() => Endpoint.Stop(Debug.Log);
    }
}
