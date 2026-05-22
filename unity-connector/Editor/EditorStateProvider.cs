using UnityEditor;

namespace UnityCliConnector
{
    public static class EditorStateProvider
    {
        public static EditorStateSnapshot Capture()
        {
            var compiling = EditorApplication.isCompiling;
            var playing = EditorApplication.isPlaying;
            return new EditorStateSnapshot
            {
                IsCompiling = compiling,
                IsPlaying = playing,
                ReadyForTools = !compiling,
            };
        }

        public static System.Collections.Generic.Dictionary<string, object> ToManifestObject()
        {
            var state = Capture();
            var activeJob = FindActiveJobId();
            return new System.Collections.Generic.Dictionary<string, object>
            {
                ["is_playing"] = state.IsPlaying,
                ["is_compiling"] = state.IsCompiling,
                ["ready_for_tools"] = state.ReadyForTools,
                ["blocking_reasons"] = BuildBlockingReasons(state),
                ["active_job"] = activeJob,
            };
        }

        private static string FindActiveJobId()
        {
            foreach (var pair in JobManager.AllJobs)
            {
                var job = pair.Value;
                if (job.Status is JobStatus.Pending or JobStatus.Running)
                    return job.Id;
            }

            return null;
        }

        private static string[] BuildBlockingReasons(EditorStateSnapshot state)
        {
            if (state.IsCompiling)
                return new[] { "compiling" };
            return System.Array.Empty<string>();
        }
    }
}
