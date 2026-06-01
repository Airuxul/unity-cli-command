using System;
using System.Collections.Generic;
using UnityCliConnector.Http;

namespace UnityCliConnector
{
    internal sealed class EditorHttpHealth : IHealthMetadataProvider
    {
        public static readonly EditorHttpHealth Instance = new();

        public void AppendHealth(Dictionary<string, object> payload)
        {
            // Must not touch EditorApplication here — /health runs on HTTP worker threads while
            // the main thread may be blocked in HttpProbe.TryGetHealth (supervisor self-check).
            payload["session_id"] = EditorHttpSession.SessionId;
            payload["generation"] = EditorHttpSession.Generation;
            payload["ready"] = EditorHttpSession.CatalogReady && !EditorPlayState.IsCompiling;

            if (EditorPlayState.IsCompiling)
                payload["blocking_reasons"] = new[] { "compiling" };
            else if (EditorPlayState.IsPlaying)
                payload["blocking_reasons"] = new[] { "playing" };
            else
                payload["blocking_reasons"] = Array.Empty<string>();
        }
    }
}
