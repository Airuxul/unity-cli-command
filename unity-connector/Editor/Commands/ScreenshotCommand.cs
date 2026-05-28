using System;
using System.Collections.Generic;
using System.IO;
using UnityCliConnector.Commands;
using UnityEditor;
using UnityEngine;
using UnityCliConnector.Params;

namespace UnityCliConnector.Commands
{
    public class ScreenshotCommand : CommandBase, ICommand<ScreenshotParams>, ICommandDescriptorProvider
    {
        public CommandDescriptor Descriptor { get; } = new CommandDescriptor<ScreenshotParams>(
            CommandNames.Screenshot,
            CommandScope.Editor,
            "Capture Scene or Game view to PNG (Editor host only)");

        private const int DefaultWidth = 1920;
        private const int DefaultHeight = 1080;

        public void Run(ScreenshotParams p)
        {
            (bool ok, object data, string error) result;
            try
            {
                var view = (p.View ?? "scene").ToLowerInvariant();
                var width = p.Width ?? DefaultWidth;
                var height = p.Height ?? DefaultHeight;
                var outputPath = ResolveOutputPath(p.OutputPath);

                var dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                switch (view)
                {
                    case "scene":
                        result = CaptureScene(width, height, outputPath);
                        break;
                    case "game":
                        result = CaptureGame(width, height, outputPath);
                        break;
                    default:
                        result = Fail($"Unknown view '{view}'. Valid: scene, game.");
                        break;
                }
            }
            catch (Exception ex)
            {
                result = Fail($"Screenshot failed: {ex.Message}");
            }

            if (result.ok)
                CompleteSuccess(CommandResult.Ok("screenshot captured", result.data));
            else
                CompleteFail(result.error);
        }

        private static (bool ok, object data, string error) CaptureScene(int width, int height, string outputPath)
        {
            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null)
                return Fail("No active SceneView found.");

            var camera = sceneView.camera;
            if (camera == null)
                return Fail("SceneView camera is null.");

            return CaptureCamera(camera, width, height, outputPath);
        }

        private static (bool ok, object data, string error) CaptureGame(int width, int height, string outputPath)
        {
            var camera = Camera.main;
            if (camera == null)
                camera = UnityEngine.Object.FindObjectOfType<Camera>();

            if (camera == null)
            {
                var hint = Application.isPlaying
                    ? "No camera in Play Mode. Tag a camera as MainCamera or add any Camera to the scene."
                    : "No camera found in scene.";
                return Fail(hint);
            }

            return CaptureCamera(camera, width, height, outputPath);
        }

        private static (bool ok, object data, string error) CaptureCamera(Camera camera, int width, int height, string outputPath)
        {
            var previousRt = camera.targetTexture;
            RenderTexture rt = null;
            Texture2D tex = null;

            try
            {
                rt = new RenderTexture(width, height, 24);
                camera.targetTexture = rt;
                camera.Render();

                RenderTexture.active = rt;
                tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                File.WriteAllBytes(outputPath, tex.EncodeToPNG());

                return Success(new Dictionary<string, object>
                {
                    ["path"] = outputPath,
                    ["width"] = width,
                    ["height"] = height,
                });
            }
            finally
            {
                camera.targetTexture = previousRt;
                RenderTexture.active = null;
                if (rt != null)
                    UnityEngine.Object.DestroyImmediate(rt);
                if (tex != null)
                    UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        private static string ResolveOutputPath(string userPath)
        {
            if (string.IsNullOrEmpty(userPath))
                userPath = "Screenshots/screenshot.png";

            if (Path.IsPathRooted(userPath))
                return Path.GetFullPath(userPath);

            var projectRoot = Path.GetDirectoryName(Application.dataPath);
            return Path.GetFullPath(Path.Combine(projectRoot, userPath));
        }

        private static (bool ok, object data, string error) Success(object data) => (true, data, null);
        private static (bool ok, object data, string error) Fail(string error) => (false, null, error);

    }
}
