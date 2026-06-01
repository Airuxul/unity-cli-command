using UnityEditor;

namespace UnityCliConnector
{
    /// <summary>Starts/stops <see cref="EditorPlayHttpHost"/> with Editor Play Mode.</summary>
    [InitializeOnLoad]
    internal static class EditorPlayHttpBootstrap
    {
        static EditorPlayHttpBootstrap()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            if (EditorApplication.isPlaying)
                EditorPlayHttpHost.Start();
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            // Defer HTTP lifecycle off the PlayMode callback to avoid blocking Enter/Exit Play.
            if (state == PlayModeStateChange.EnteredPlayMode)
                EditorApplication.delayCall += static () => EditorPlayHttpHost.Start();
            else if (state is PlayModeStateChange.ExitingPlayMode or PlayModeStateChange.EnteredEditMode)
                EditorApplication.delayCall += static () => EditorPlayHttpHost.Stop();
        }
    }
}
