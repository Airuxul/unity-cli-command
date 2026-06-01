using System.Collections.Generic;
using UnityCliConnector.Params;
using UnityEditor;
using UnityEngine.SceneManagement;

namespace UnityCliConnector.Editor.Services
{
    public static class AssetRefreshService
    {
        public static Dictionary<string, object> Refresh(RefreshParams p)
        {
            p ??= new RefreshParams();

            if (!p.Force && EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new System.InvalidOperationException(
                    "Cannot refresh while Unity is in or entering play mode. Exit play mode first, or pass force=true.");
            }

            var options = p.Force
                ? ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport
                : ImportAssetOptions.ForceSynchronousImport;

            AssetDatabase.Refresh(options);

            var compileRequested = p.Compile;

            var dirtyScenes = GetDirtyOpenScenes();
            string warning = null;
            if (dirtyScenes.Count > 0)
            {
                warning =
                    "Open scenes have unsaved changes. Unity may show a modal if scenes changed on disk.";
            }

            return new Dictionary<string, object>
            {
                ["refreshed"] = true,
                ["compile_requested"] = compileRequested,
                ["force"] = p.Force,
                ["warning"] = warning,
                ["dirty_scenes"] = dirtyScenes,
            };
        }

        private static List<string> GetDirtyOpenScenes()
        {
            var scenes = new List<string>();
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isDirty)
                    continue;
                scenes.Add(string.IsNullOrEmpty(scene.path) ? scene.name : scene.path);
            }

            return scenes;
        }
    }
}
