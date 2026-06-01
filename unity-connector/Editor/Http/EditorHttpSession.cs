using System;
using UnityEditor;

namespace UnityCliConnector
{
    /// <summary>Per-domain Editor HTTP identity and readiness (survives Play, resets on script reload).</summary>
    internal static class EditorHttpSession
    {
        private const string SessionKey = "UnityCliConnector.EditorHttp.SessionId";
        private const string GenerationKey = "UnityCliConnector.EditorHttp.Generation";

        public static string SessionId { get; private set; }
        public static int Generation { get; private set; }
        public static bool CatalogReady { get; private set; }

        public static void BeginDomain()
        {
            SessionId = Guid.NewGuid().ToString("N");
            Generation = SessionState.GetInt(GenerationKey, 0) + 1;
            SessionState.SetString(SessionKey, SessionId);
            SessionState.SetInt(GenerationKey, Generation);
            CatalogReady = false;
            CommandCatalog.ClearCachedVersions();
        }

        public static void MarkCatalogReady() => CatalogReady = true;

        public static void MarkCatalogNotReady() => CatalogReady = false;
    }
}
