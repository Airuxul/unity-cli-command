using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace UnityCliConnector
{
    [InitializeOnLoad]
    public static class HeartbeatWriter
    {
        public const int ProtocolVersion = 1;
        public static int Port { get; private set; }
        public static string Host { get; private set; } = "127.0.0.1";

        private static double _nextWrite;

        static HeartbeatWriter()
        {
            EditorApplication.update += OnUpdate;
        }

        public static void SetEndpoint(string host, int port)
        {
            Host = string.IsNullOrEmpty(host) ? "127.0.0.1" : host;
            Port = port;
            WriteNow();
        }

        private static void OnUpdate()
        {
            if (Port <= 0)
                return;
            if (EditorApplication.timeSinceStartup < _nextWrite)
                return;
            _nextWrite = EditorApplication.timeSinceStartup + 0.5;
            WriteNow();
        }

        public static void WriteNow()
        {
            if (Port <= 0)
                return;

            try
            {
                var projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                var editorState = EditorStateProvider.ToManifestObject();
                var manifest = new Dictionary<string, object>
                {
                    ["project_path"] = projectPath,
                    ["host"] = Host,
                    ["port"] = Port,
                    ["protocol_version"] = ProtocolVersion,
                    ["editor_state"] = editorState,
                    ["updated_at"] = DateTime.UtcNow.ToString("o"),
                };

                var dir = GetInstancesDirectory();
                Directory.CreateDirectory(dir);
                var file = Path.Combine(dir, HashProject(projectPath) + ".json");
                File.WriteAllText(file, SimpleJson.Serialize(manifest), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[unity-connector] heartbeat write failed: {ex.Message}");
            }
        }

        public static string GetInstancesDirectory()
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".unity-cmd", "instances");
        }

        private static string HashProject(string path)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(path.ToLowerInvariant()));
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant()[..16];
        }

    }
}
