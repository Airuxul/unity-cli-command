using UnityEditor;

namespace UnityCliConnector
{
    /// <summary>Cached Editor flags safe to read from HTTP worker threads.</summary>
    [InitializeOnLoad]
    public static class EditorPlayState
    {
        public static volatile bool IsPlaying;
        public static volatile bool IsCompiling;

        static EditorPlayState()
        {
            EditorApplication.playModeStateChanged += _ => Refresh();
            EditorApplication.update += Refresh;
            EditorApplication.delayCall += Refresh;
        }

        private static void Refresh()
        {
            IsPlaying = EditorApplication.isPlaying;
            IsCompiling = EditorApplication.isCompiling;
        }
    }
}
