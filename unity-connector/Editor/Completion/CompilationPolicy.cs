namespace UnityCliConnector.Completion
{
    public sealed class CompilationPolicy : ICompletionPolicy
    {
        public string Kind => CommandJobCatalog.CompletionCompilation;

        public bool TryComplete(JobRecord job, EditorStateSnapshot state, out object result, out string error)
        {
            result = null;
            error = null;

            if (state.IsCompiling)
            {
                if (job.Status == JobStatus.Pending)
                    job.Status = JobStatus.Running;
                return false;
            }

            if (job.Status == JobStatus.Running)
            {
                result = new System.Collections.Generic.Dictionary<string, object> { ["compiled"] = true };
                return true;
            }

            if (job.Status == JobStatus.Pending)
            {
                job.Status = JobStatus.Running;
                result = new System.Collections.Generic.Dictionary<string, object>
                {
                    ["compiled"] = true,
                    ["note"] = "already_idle",
                };
                return true;
            }

            return false;
        }
    }
}
